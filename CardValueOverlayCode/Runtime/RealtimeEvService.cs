using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CardValueOverlay.Modeling.Estimation;
using CardValueOverlay.Modeling.Extraction;
using CardValueOverlay.Modeling.Simulation;
using GodotFileAccess = Godot.FileAccess;

namespace CardValueOverlay.CardValueOverlayCode.Runtime;

/// <summary>
/// Runs the Monte Carlo deck simulator in-game on a background thread to compute the
/// deck-contextual EV of adding a card. Everything is wrapped so a failure degrades to
/// "no value" and never crashes or blocks the game. Results fill progressively
/// (short -> mid -> long) so the overlay can show "calculating..." then real numbers.
/// </summary>
public static class RealtimeEvService
{
    // Horizons: short / mid / long turn counts (match the training convention 4/8/14).
    private static readonly int[] Horizons = [4, 8, 14];
    private const int RunsPerSim = 48;      // Monte Carlo runs per simulation (tunable; safe to raise).
    private const int MaxBranch = 4;        // student beam width (branch-4; raise/lower for quality vs speed).
    private const int ReservedCores = 3;    // cores left free for the game so compute doesn't stutter it.
    private const int HandSize = 5;
    private const int MaxHandSize = 10;
    private const int BaseEnergy = 3;
    private const int BaseStars = 3;
    private const int MaxCardsPlayedPerTurn = 8;

    public sealed class CardEvResult
    {
        // "calculated" column = (normalEV - blockedEV) / plays = value per direct play (the live
        // analog of the precomputed estimate). null = still computing.
        public double? CalcShort;
        public double? CalcMid;
        public double? CalcLong;

        // "ΔEV" column = normalEV - baselineEV = deck-level EV change from adding the card.
        public double? DeltaShort;
        public double? DeltaMid;
        public double? DeltaLong;

        // Second table: whole-deck total EV before/after adding the card.
        // "total" = baseline EV (deck without the card); "after" = normal EV (deck with it played).
        public double? BaselineShort;
        public double? BaselineMid;
        public double? BaselineLong;
        public double? AfterShort;
        public double? AfterMid;
        public double? AfterLong;

        public volatile bool Failed;
    }

    // ---- caches ----
    private static readonly object dataLock = new();
    private static bool dataLoaded;
    private static bool dataFailed;
    private static IReadOnlyList<CardFactCatalogEntry> factEntries = [];
    private static ValueCalibration? calibration;
    private static IReadOnlyList<CardPoolMembershipEntry> memberships = [];
    private static GeneratedCardPoolCatalog generatedPools = GeneratedCardPoolCatalog.Empty;
    private static SimulationSetupPriorityCatalog setupPriorities = SimulationSetupPriorityCatalog.Empty;

    private static readonly ConcurrentDictionary<int, LibraryForLayer> librariesByLayer = new();
    private static readonly ConcurrentDictionary<string, CardEvResult> results = new();
    // Per-turn baseline EV (deck WITHOUT the card), keyed by deck signature and shared across
    // all offered cards for the same deck.
    private static readonly ConcurrentDictionary<string, double[]> baselineByDeck = new();
    private static readonly ConcurrentQueue<WorkItem> queue = new();
    private static int workerRunning; // 0/1 via Interlocked

    /// True while any card EV is still being computed; the overlay refresh loop uses this
    /// to keep re-rendering until the async results are ready.
    public static bool HasPendingWork => !queue.IsEmpty || Volatile.Read(ref workerRunning) == 1;

    // "Traceless" background compute: the worker runs continuously, but during combat it uses only a
    // few cores (ProcessorCount/4) so it never starves the game; outside combat it uses many cores
    // (ProcessorCount - ReservedCores) for speed. Set by combat start/end Harmony patches.
    private static volatile bool inCombat;

    // The signature of the deck/floor the UI currently cares about. Work queued for a different
    // signature (e.g. computed for floor 14 but you already moved to floor 16) is stale and skipped.
    private static volatile string currentSignature = "";

    // Cores for the background sim, adaptive: few during combat (imperceptible), many otherwise.
    private static int CurrentRunDegree() =>
        inCombat
            ? Math.Max(1, Environment.ProcessorCount / 4)
            : Math.Max(2, Environment.ProcessorCount - ReservedCores);

    public static void OnCombatStart()
    {
        inCombat = true;
        // Use the (few-core, imperceptible) combat time to precompute the deck's baseline EV, so the
        // card-reward screen that follows fills in fast.
        try
        {
            DeckSnapshot? snapshot = GetSnapshot();
            if (snapshot is not null && !baselineByDeck.ContainsKey(snapshot.Signature))
            {
                queue.Enqueue(new WorkItem($"baseline|{snapshot.Signature}", snapshot.Signature, snapshot.Layer, snapshot.Cards, null, 0));
                EnsureWorker();
            }
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"OnCombatStart baseline precompute failed: {ex.Message}", 0);
        }
    }

    public static void OnCombatEnd()
    {
        // Combat over: the worker (still running) switches back to many cores; any leftover stale
        // work is skipped once the next overlay render updates currentSignature.
        inCombat = false;
        if (!queue.IsEmpty)
        {
            EnsureWorker();
        }
    }

    // The live deck is read on the MAIN thread and cached briefly, so the per-frame render path
    // (called many times/sec during refresh polling) doesn't re-walk the deck + rebuild a
    // signature string every call. The deck only changes when a card is added (rare), so a short
    // TTL is safe. The snapshot is also passed into the worker so the background thread never
    // touches live game state.
    private static readonly object snapshotLock = new();
    private static DeckSnapshot? cachedSnapshot;
    private static long cachedSnapshotTick;
    private const long SnapshotTtlMs = 250;

    private static DeckSnapshot? GetSnapshot()
    {
        DeckSnapshot? result = ReadSnapshotCached();
        if (result is not null)
        {
            // Track the context the UI is looking at now; the worker skips work for other signatures.
            currentSignature = result.Signature;
        }

        return result;
    }

    private static DeckSnapshot? ReadSnapshotCached()
    {
        long now = Environment.TickCount64;
        DeckSnapshot? snap = cachedSnapshot;
        if (snap is not null && now - cachedSnapshotTick < SnapshotTtlMs)
        {
            return snap;
        }

        lock (snapshotLock)
        {
            now = Environment.TickCount64;
            if (cachedSnapshot is not null && now - cachedSnapshotTick < SnapshotTtlMs)
            {
                return cachedSnapshot;
            }

            DeckSnapshot? fresh = LiveDeck.TryRead();
            if (fresh is not null)
            {
                cachedSnapshot = fresh;
                cachedSnapshotTick = now;
            }

            return fresh ?? cachedSnapshot;
        }
    }

    /// Warm up the one-time modeling data load + library build on a background thread, so the
    /// first card view doesn't pay that spike. Safe to call repeatedly (no-op once loaded).
    public static void Prefetch()
    {
        if (dataLoaded || dataFailed)
        {
            return;
        }

        Task.Run(() =>
        {
            try
            {
                EnsureDataLoaded();
            }
            catch
            {
                // EnsureDataLoaded already logs; never surface.
            }
        });
    }

    private sealed record LibraryForLayer(
        IReadOnlyList<SimulationCard> Library,
        IReadOnlyDictionary<string, SimulationCard> ByModelId);

    // ProbeId == null means "baseline only" (warm the deck's baseline EV, no card).
    private sealed record WorkItem(
        string ResultKey,
        string DeckSignature,
        int Layer,
        IReadOnlyList<DeckCardRef> DeckCards,
        string? ProbeId,
        int ProbeUpgrade);

    /// <summary>
    /// Returns the current (possibly still-computing) EV result for adding this card to the
    /// live deck, queueing a background computation if none exists yet. Returns null when no
    /// run is active or the live deck can't be read. Never throws.
    /// </summary>
    public static CardEvResult? RequestCardEv(string probeModelId, int probeUpgrade)
    {
        try
        {
            if (string.IsNullOrEmpty(probeModelId))
            {
                return null;
            }

            DeckSnapshot? snapshot = GetSnapshot();
            if (snapshot is null)
            {
                return null;
            }

            CardEvResult result = EnqueueCard(snapshot, probeModelId, probeUpgrade);
            EnsureWorker();
            return result;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"RealtimeEvService.RequestCardEv failed: {ex.Message}", 0);
            return null;
        }
    }

    private static CardEvResult EnqueueCard(DeckSnapshot snapshot, string cardId, int upgrade)
    {
        string resultKey = $"{snapshot.Signature}|{cardId}+{upgrade}";
        if (results.TryGetValue(resultKey, out CardEvResult? existing))
        {
            return existing;
        }

        CardEvResult created = new();
        if (!results.TryAdd(resultKey, created))
        {
            return results[resultKey];
        }

        queue.Enqueue(new WorkItem(resultKey, snapshot.Signature, snapshot.Layer, snapshot.Cards, cardId, upgrade));
        return created;
    }

    /// Proactive precompute: value every distinct card already in the deck, so browsing the
    /// deck-view screen is instant. Trigger on deck-view open.
    public static void PrecomputeDeckCards()
    {
        try
        {
            DeckSnapshot? snapshot = GetSnapshot();
            if (snapshot is null)
            {
                return;
            }

            HashSet<string> seen = new(StringComparer.Ordinal);
            foreach (DeckCardRef card in snapshot.Cards)
            {
                if (seen.Add($"{card.Id}+{card.Upgrade}"))
                {
                    EnqueueCard(snapshot, card.Id, card.Upgrade);
                }
            }

            EnsureWorker();
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"RealtimeEvService.PrecomputeDeckCards failed: {ex.Message}", 0);
        }
    }

    /// Hook for future event-reward precompute (offered cards that are NOT in the deck). Unwired
    /// for now — call this with the event's offered cards once event detection is added.
    public static void PrecomputeForEvent(IEnumerable<(string Id, int Upgrade)> offeredCards)
    {
        try
        {
            DeckSnapshot? snapshot = GetSnapshot();
            if (snapshot is null)
            {
                return;
            }

            foreach ((string id, int upgrade) in offeredCards)
            {
                EnqueueCard(snapshot, id, upgrade);
            }

            EnsureWorker();
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"RealtimeEvService.PrecomputeForEvent failed: {ex.Message}", 0);
        }
    }

    private static void EnsureWorker()
    {
        if (Interlocked.CompareExchange(ref workerRunning, 1, 0) != 0)
        {
            return;
        }

        Task.Run(() =>
        {
            try
            {
                DrainQueue();
            }
            catch (Exception ex)
            {
                MainFile.Logger.Warn($"RealtimeEvService worker crashed: {ex.Message}", 0);
            }
            finally
            {
                Interlocked.Exchange(ref workerRunning, 0);
                // A late enqueue between drain end and flag reset: restart if needed.
                if (!queue.IsEmpty)
                {
                    EnsureWorker();
                }
            }
        });
    }

    private static void DrainQueue()
    {
        if (!EnsureDataLoaded())
        {
            // Mark everything queued as failed so the UI stops showing "calculating..." forever.
            while (queue.TryDequeue(out WorkItem? item))
            {
                if (results.TryGetValue(item.ResultKey, out CardEvResult? r))
                {
                    r.Failed = true;
                }
            }

            return;
        }

        // Runs continuously (during combat too, but on few cores via CurrentRunDegree so it's
        // imperceptible). Stale items — for a deck/floor the UI has already moved past — are skipped.
        while (queue.TryDequeue(out WorkItem? item))
        {
            if (!string.Equals(item.DeckSignature, currentSignature, StringComparison.Ordinal))
            {
                continue;
            }

            ComputeOne(item);
        }
    }

    private static void ComputeOne(WorkItem item)
    {
        if (item.ProbeId is null)
        {
            ComputeBaselineOnly(item);
            return;
        }

        if (!results.TryGetValue(item.ResultKey, out CardEvResult? result))
        {
            return;
        }

        try
        {
            LibraryForLayer? lib = GetLibrary(item.Layer);
            if (lib is null)
            {
                result.Failed = true;
                return;
            }

            // Use the deck snapshot captured on the main thread (no off-thread game-state read).
            List<SimulationCard> deck = MapDeck(item.DeckCards, lib);
            if (deck.Count == 0)
            {
                result.Failed = true;
                return;
            }

            SimulationCard? probe = MapCard(item.ProbeId!, item.ProbeUpgrade, lib);
            if (probe is null)
            {
                result.Failed = true;
                return;
            }

            // Few cores during combat (imperceptible), many otherwise (fast).
            int runDegree = CurrentRunDegree();
            int maxTurns = Horizons[^1];
            string probeId = probe.ModelId;
            List<SimulationCard> deckWithProbe = [.. deck, probe];
            DeckMonteCarloSimulator simulator = new();

            // 1) baseline EV: deck WITHOUT the card (per-turn), shared across offered cards.
            double[] baselineByTurn = baselineByDeck.GetOrAdd(
                item.DeckSignature,
                _ => SimulatePerTurn(deck, maxTurns, lib, runDegree));

            // 2) block EV: card in deck, drawn but never played. 3) normal EV: card played.
            //    Same seed everywhere => paired sampling (common random numbers).
            TrackedCardSimulationReport normal = simulator.SimulateTrackedCard(
                deckWithProbe, BuildOptions(maxTurns, lib, runDegree, blockedProbeId: null), probeId, collectCredits: false);
            TrackedCardSimulationReport blocked = simulator.SimulateTrackedCard(
                deckWithProbe, BuildOptions(maxTurns, lib, runDegree, blockedProbeId: probeId), probeId, collectCredits: false);

            for (int i = 0; i < Horizons.Length; i++)
            {
                int turns = Horizons[i];
                decimal normalEv = SumExpectedValue(normal, turns);
                decimal blockedEv = SumExpectedValue(blocked, turns);
                int plays = SumPlayCount(normal, turns);
                double baseline = SumBaseline(baselineByTurn, turns);

                double? valuePerPlay = plays == 0
                    ? null
                    : (double)((normalEv - blockedEv) * RunsPerSim / plays);
                double after = (double)normalEv;
                double deltaEv = after - baseline;

                switch (i)
                {
                    case 0:
                        result.CalcShort = valuePerPlay; result.DeltaShort = deltaEv;
                        result.BaselineShort = baseline; result.AfterShort = after;
                        break;
                    case 1:
                        result.CalcMid = valuePerPlay; result.DeltaMid = deltaEv;
                        result.BaselineMid = baseline; result.AfterMid = after;
                        break;
                    case 2:
                        result.CalcLong = valuePerPlay; result.DeltaLong = deltaEv;
                        result.BaselineLong = baseline; result.AfterLong = after;
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            result.Failed = true;
            MainFile.Logger.Warn($"RealtimeEvService compute failed for {item.ProbeId}: {ex.Message}", 0);
        }
    }

    private static void ComputeBaselineOnly(WorkItem item)
    {
        try
        {
            if (baselineByDeck.ContainsKey(item.DeckSignature))
            {
                return;
            }

            LibraryForLayer? lib = GetLibrary(item.Layer);
            if (lib is null)
            {
                return;
            }

            List<SimulationCard> deck = MapDeck(item.DeckCards, lib);
            if (deck.Count == 0)
            {
                return;
            }

            baselineByDeck.GetOrAdd(item.DeckSignature, _ => SimulatePerTurn(deck, Horizons[^1], lib, CurrentRunDegree()));
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"RealtimeEvService.ComputeBaselineOnly failed: {ex.Message}", 0);
        }
    }

    private static double[] SimulatePerTurn(
        IReadOnlyList<SimulationCard> deck,
        int turns,
        LibraryForLayer lib,
        int runDegree)
    {
        IReadOnlyList<decimal> values = new DeckMonteCarloSimulator()
            .SimulateExpectedTurnValues(deck, BuildOptions(turns, lib, runDegree, blockedProbeId: null));
        double[] perTurn = new double[turns];
        for (int t = 0; t < turns && t < values.Count; t++)
        {
            perTurn[t] = (double)values[t];
        }

        return perTurn;
    }

    private static DeckSimulationOptions BuildOptions(
        int turns,
        LibraryForLayer lib,
        int runDegree,
        string? blockedProbeId)
    {
        return new DeckSimulationOptions
        {
            Turns = turns,
            Runs = RunsPerSim,
            RunDegreeOfParallelism = runDegree,
            // Fixed seed across baseline/block/normal => common random numbers (paired sampling),
            // so the per-play delta and ΔEV are far less noisy than the absolute EVs.
            Seed = 20260705,
            HandSize = HandSize,
            MaxHandSize = MaxHandSize,
            BaseEnergy = BaseEnergy,
            BaseStars = BaseStars,
            StarsPersistBetweenTurns = true,
            MaxCardsPlayedPerTurn = MaxCardsPlayedPerTurn,
            MaxBranchingCards = MaxBranch,
            CardLibrary = lib.Library,
            GeneratedCardPools = generatedPools,
            BlockedPlayModelIds = blockedProbeId is null ? [] : [blockedProbeId]
        };
    }

    private static decimal SumExpectedValue(TrackedCardSimulationReport report, int turns)
    {
        decimal sum = 0m;
        foreach (TrackedCardTurnSummary turn in report.Turns)
        {
            if (turn.Turn <= turns)
            {
                sum += turn.ExpectedValue;
            }
        }

        return sum;
    }

    private static int SumPlayCount(TrackedCardSimulationReport report, int turns)
    {
        int sum = 0;
        foreach (TrackedCardTurnSummary turn in report.Turns)
        {
            if (turn.Turn <= turns)
            {
                sum += turn.PlayCount;
            }
        }

        return sum;
    }

    private static double SumBaseline(double[] perTurn, int turns)
    {
        double sum = 0d;
        for (int t = 0; t < turns && t < perTurn.Length; t++)
        {
            sum += perTurn[t];
        }

        return sum;
    }

    private static List<SimulationCard> MapDeck(IReadOnlyList<DeckCardRef> cards, LibraryForLayer lib)
    {
        List<SimulationCard> deck = [];
        int skipped = 0;
        foreach (DeckCardRef card in cards)
        {
            SimulationCard? mapped = MapCard(card.Id, card.Upgrade, lib);
            if (mapped is null)
            {
                skipped++;
                continue;
            }

            deck.Add(mapped);
        }

        if (skipped > 0)
        {
            MainFile.Logger.Info($"RealtimeEvService: {skipped} deck cards not in simulation library (skipped).", 0);
        }

        return deck;
    }

    private static SimulationCard? MapCard(string id, int upgrade, LibraryForLayer lib)
    {
        string key = upgrade > 0 ? $"{id}+{upgrade}" : id;
        if (lib.ByModelId.TryGetValue(key, out SimulationCard? match))
        {
            return match;
        }

        // Fall back to the unupgraded form if the exact upgrade level isn't modeled.
        return lib.ByModelId.TryGetValue(id, out SimulationCard? unupgraded) ? unupgraded : null;
    }

    private static LibraryForLayer? GetLibrary(int layer)
    {
        try
        {
            return librariesByLayer.GetOrAdd(layer, l =>
            {
                IReadOnlyList<SimulationCard> library = new SimulationCardLibraryBuilder().Build(
                    factEntries,
                    calibration!,
                    l,
                    includeUpgrades: true,
                    memberships,
                    setupPriorities);
                Dictionary<string, SimulationCard> byModelId = library
                    .GroupBy(card => card.ModelId, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
                return new LibraryForLayer(library, byModelId);
            });
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"RealtimeEvService: failed to build library for layer {layer}: {ex.Message}", 0);
            return null;
        }
    }

    private static bool EnsureDataLoaded()
    {
        if (dataLoaded)
        {
            return true;
        }

        if (dataFailed)
        {
            return false;
        }

        lock (dataLock)
        {
            if (dataLoaded)
            {
                return true;
            }

            if (dataFailed)
            {
                return false;
            }

            try
            {
                JsonSerializerOptions options = new(JsonSerializerDefaults.Web)
                {
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                };

                factEntries = JsonSerializer.Deserialize<List<CardFactCatalogEntry>>(
                    ReadResource("card_facts.generated.json"), options)
                    ?? throw new InvalidOperationException("card_facts deserialized to null.");
                calibration = JsonSerializer.Deserialize<ValueCalibration>(
                    ReadResource("model_calibration.json"), options)
                    ?? throw new InvalidOperationException("model_calibration deserialized to null.");
                memberships = JsonSerializer.Deserialize<List<CardPoolMembershipEntry>>(
                    ReadResource("card_pool_memberships.generated.json"), options)
                    ?? [];
                generatedPools = JsonSerializer.Deserialize<GeneratedCardPoolCatalog>(
                    ReadResource("simulation_generated_card_pools.json"), options)
                    ?? GeneratedCardPoolCatalog.Empty;
                setupPriorities = JsonSerializer.Deserialize<SimulationSetupPriorityCatalog>(
                    ReadResource("simulation_setup_priorities.json"), options)
                    ?? SimulationSetupPriorityCatalog.Empty;

                dataLoaded = true;
                MainFile.Logger.Info(
                    $"RealtimeEvService: modeling data loaded (facts={factEntries.Count}, memberships={memberships.Count}).",
                    0);
                return true;
            }
            catch (Exception ex)
            {
                dataFailed = true;
                MainFile.Logger.Warn($"RealtimeEvService: failed to load modeling data: {ex}", 0);
                return false;
            }
        }
    }

    private static string ReadResource(string fileName)
    {
        string path = $"res://CardValueOverlay/data/modeling/{fileName}";
        if (!GodotFileAccess.FileExists(path))
        {
            throw new FileNotFoundException($"Modeling resource not found: {path}");
        }

        using GodotFileAccess file = GodotFileAccess.Open(path, GodotFileAccess.ModeFlags.Read)
            ?? throw new InvalidOperationException($"Unable to open modeling resource: {path}");
        byte[] bytes = file.GetBuffer((long)file.GetLength());
        return Encoding.UTF8.GetString(bytes);
    }

    // ---- live deck snapshot (game API isolated + exception-safe) ----

    public readonly record struct DeckCardRef(string Id, int Upgrade);

    public sealed record DeckSnapshot(IReadOnlyList<DeckCardRef> Cards, int Layer, string Signature);

    internal static class LiveDeck
    {
        public static DeckSnapshot? TryRead()
        {
            try
            {
                MegaCrit.Sts2.Core.Runs.RunManager? manager = MegaCrit.Sts2.Core.Runs.RunManager.Instance;
                if (manager is null || !manager.IsInProgress)
                {
                    return null;
                }

                MegaCrit.Sts2.Core.Runs.RunState? state = manager.DebugOnlyGetState();
                if (state is null || state.Players.Count == 0)
                {
                    return null;
                }

                MegaCrit.Sts2.Core.Entities.Players.Player player = state.Players[0];
                IReadOnlyList<MegaCrit.Sts2.Core.Models.CardModel> cards = player.Deck.Cards;

                List<DeckCardRef> refs = new(cards.Count);
                foreach (MegaCrit.Sts2.Core.Models.CardModel card in cards)
                {
                    string id = card.Id.ToString();
                    if (string.IsNullOrEmpty(id))
                    {
                        continue;
                    }

                    refs.Add(new DeckCardRef(id, card.CurrentUpgradeLevel));
                }

                if (refs.Count == 0)
                {
                    return null;
                }

                int layer = ResolveLayer(state);
                string signature = BuildSignature(refs, layer);
                return new DeckSnapshot(refs, layer, signature);
            }
            catch (Exception ex)
            {
                MainFile.Logger.Warn($"RealtimeEvService.LiveDeck.TryRead failed: {ex.Message}", 0);
                return null;
            }
        }

        private static int ResolveLayer(MegaCrit.Sts2.Core.Runs.RunState state)
        {
            try
            {
                int floor = state.TotalFloor;
                return floor > 0 ? floor : 17;
            }
            catch
            {
                return 17;
            }
        }

        private static string BuildSignature(List<DeckCardRef> refs, int layer)
        {
            List<string> tokens = refs
                .Select(r => r.Upgrade > 0 ? $"{r.Id}+{r.Upgrade}" : r.Id)
                .ToList();
            tokens.Sort(StringComparer.Ordinal);
            return $"L{layer}:" + string.Join(",", tokens);
        }
    }
}
