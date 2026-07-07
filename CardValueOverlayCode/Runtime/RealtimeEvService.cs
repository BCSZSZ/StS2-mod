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
    private const int RunsPerSim = 36;      // Monte Carlo runs per simulation (all horizons share one pass).
    private const int MaxBranch = 3;        // student beam width (branch-3; raise/lower for quality vs speed).
    private const int ReservedCores = 3;    // cores left free for the game so compute doesn't stutter it.
    private const int HandSize = 5;
    private const int MaxHandSize = 10;
    private const int BaseEnergy = 3;
    private const int BaseStars = 3;
    private const int MaxCardsPlayedPerTurn = 8;
    private const int SimulationSeed = 20260705; // fixed so results are deterministic + cache-comparable
    private const int DefaultLayerFallback = 17; // Act 2/3 pressure band, used when TotalFloor is unavailable

    public sealed class CardEvResult
    {
        // "calculated" column = (normalEV - blockedEV) / plays = value per direct play (the live
        // analog of the precomputed estimate). null = still computing.
        public double? CalcShort;
        public double? CalcMid;
        public double? CalcLong;

        // "dEV" column = normalEV - baselineEV = deck-level EV change from adding the card.
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

        // Set true by the worker AFTER all horizons are written (success path). Needed because a
        // legitimately-computed card can still have null calc values (e.g. plays == 0 / unplayable),
        // so "value is null" cannot mean "still computing". Settled = the overlay can stop waiting.
        public volatile bool Complete;

        // Coarse per-card progress 0..3 (normal sim done -> 1, blocked done -> 2, all horizons -> 3),
        // so the progress bar advances smoothly within a card instead of only per-card.
        public volatile int ProgressStage;

        public bool IsSettled => Complete || Failed;
    }

    // ---- caches ----
    private static readonly object dataLock = new();
    private static bool dataLoaded;
    private static bool dataFailed;
    private static IReadOnlyList<CardFactCatalogEntry> factEntries = [];
    private static ValueCalibration? calibration;
    private static IReadOnlyList<CardPoolMembershipEntry> memberships = [];
    private static GeneratedCardPoolCatalog generatedPools = GeneratedCardPoolCatalog.Empty;
    private static CardSetupValueCatalog cardSetupValues = CardSetupValueCatalog.Empty;

    private static readonly ConcurrentDictionary<int, LibraryForLayer> librariesByLayer = new();
    private static readonly ConcurrentDictionary<string, CardEvResult> results = new();
    // Per-turn baseline EV (deck WITHOUT the card), keyed by deck signature and shared across
    // all offered cards for the same deck.
    private static readonly ConcurrentDictionary<string, double[]> baselineByDeck = new();
    private static readonly ConcurrentQueue<WorkItem> queue = new();
    // Result keys currently queued or being computed. Lets EnqueueCard re-queue orphaned work
    // (placeholder exists but its work was dropped/lost) without double-queueing live work.
    private static readonly ConcurrentDictionary<string, byte> inFlight = new();
    private static int workerRunning; // 0/1 via Interlocked

    // ---- cross-restart persistence of computed results ----
    // Results/baselines are expensive to recompute, so they are mirrored to disk and reloaded on the
    // next launch. The compute key stamps the parameters/semantics the cache was built with; if it
    // changes (runs/branch/horizons, or the manual "semN" bump when the simulator's math changes)
    // the on-disk cache is ignored so stale numbers never resurface. Bump "sem" on sim changes.
    private const string CacheFilePath = "user://CardValueOverlay_calc_cache.json";
    private const int MaxPersistedEntries = 8000;
    // Hard cap on the in-memory result/baseline dictionaries so a very long / multi-run session
    // can't grow them unbounded (keys embed the whole-deck signature, so they never get reused).
    private const int MaxInMemoryEntries = 4000;
    private const long CacheSaveThrottleMs = 1500;
    // sem4: unified setup JSON is now loaded at runtime and Discovery generated cards are free this turn.
    // Bump so stale sem3 calc-cache entries never mask the current simulator/data semantics.
    private static readonly string CacheComputeKey =
        $"v1|runs{RunsPerSim}|branch{MaxBranch}|turns{Horizons[^1]}|h{string.Join('-', Horizons)}|seed{SimulationSeed}|sem4";
    private static volatile bool cacheDirty;
    private static long lastCacheSaveTick;

    // "Traceless" background compute: the worker runs continuously, but during combat it uses only a
    // few cores (ProcessorCount/4) so it never starves the game; outside combat it uses many cores
    // (ProcessorCount - ReservedCores) for speed. Set by combat start/end Harmony patches.
    private static volatile bool inCombat;

    // The signature of the deck/floor the UI currently cares about. Work queued for a different
    // signature (e.g. computed for floor 14 but you already moved to floor 16) is stale and skipped.
    private static volatile string currentSignature = "";

    // Cores for the background sim. Default = MANY cores (map / events / reward / upgrade / deck /
    // rest - none of these is a frame-critical fight, so background compute there is free speed).
    // DURING a fight we no longer collapse to a single thread: the game's hot path is one
    // render/logic thread, so a MODEST slice of the otherwise-idle cores can compute in parallel
    // without stealing the game's core. Those parallel workers are dropped to BelowNormal priority
    // (see BuildOptions -> WorkerThreadPriority) so the OS always lets the game preempt them -
    // "extra idle cores, never the game's". "In a fight" is bounded precisely by SetUpCombat (enter)
    // and the native CombatEnded event (exit) - NOT by Reset, which fires only on room exit.
    private const int CombatReservedCores = 4; // during combat, leave this many cores fully free for the game
    private static int CombatRunDegree() =>
        Math.Max(2, Math.Min(Environment.ProcessorCount / 4, Environment.ProcessorCount - CombatReservedCores));
    private static int CurrentRunDegree() =>
        inCombat
            ? CombatRunDegree()
            : Math.Max(2, Environment.ProcessorCount - ReservedCores);

    public static void OnCombatStart()
    {
        inCombat = true;
        EnsureCombatEndedSubscription();
        // Use the (BelowNormal, imperceptible) combat time to precompute the deck's baseline EV, so the
        // card-reward screen that follows fills in fast.
        try
        {
            DeckSnapshot? snapshot = GetSnapshot();
            if (snapshot is not null && !baselineByDeck.ContainsKey(snapshot.Signature))
            {
                queue.Enqueue(new WorkItem($"baseline|{snapshot.Signature}", snapshot.Signature, snapshot.Layer, snapshot.Cards, null, 0, null));
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

    // Subscribe once to the native CombatEnded event so "in combat" ends the moment the fight is
    // actually decided (win or loss) - BEFORE the reward screen. Without this, inCombat stays true
    // (via the Reset hook, which only runs on ROOM EXIT) all through the reward screen, wrongly
    // pinning the reward-time compute to the 1-core path. CombatManager.Instance is a persistent
    // singleton, so a single subscription lasts the whole process.
    private static bool combatEndedSubscribed;

    private static void EnsureCombatEndedSubscription()
    {
        if (combatEndedSubscribed)
        {
            return;
        }

        try
        {
            MegaCrit.Sts2.Core.Combat.CombatManager combat = MegaCrit.Sts2.Core.Combat.CombatManager.Instance;
            combat.CombatEnded += _ => OnCombatEnd();
            combatEndedSubscribed = true;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"RealtimeEvService: failed to subscribe to CombatEnded: {ex.Message}", 0);
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

    // Unique prefix so a probe card's modelId never collides with a real deck card. Block/tracking are
    // by modelId, so an un-prefixed probe would (wrongly) affect EVERY same-name card in the deck.
    private const string ProbeModelIdPrefix = "CARDVALUEOVERLAY.PROBE.";

    // ProbeId == null means "baseline only" (warm the deck's baseline EV, no card).
    // RemoveUpgrade: null = ADD probe to the current deck (reward: card not in deck). Non-null = the
    // card is ALREADY in the deck, so remove ONE instance of (ProbeId, RemoveUpgrade) from the
    // baseline before adding the probe back - i.e. value it as "this card in deck vs. deck without it"
    // (deck view / upgrade preview).
    private sealed record WorkItem(
        string ResultKey,
        string DeckSignature,
        int Layer,
        IReadOnlyList<DeckCardRef> DeckCards,
        string? ProbeId,
        int ProbeUpgrade,
        int? RemoveUpgrade);

    /// <summary>
    /// Returns the current (possibly still-computing) EV result for adding this card to the
    /// live deck, queueing a background computation if none exists yet. Returns null when no
    /// run is active or the live deck can't be read. Never throws.
    /// </summary>
    // removeUpgrade: null = ADD the card to the current deck (reward screen, card not yet owned).
    // Non-null = the card is already in the deck (deck view / upgrade preview): value it as "in deck
    // vs. deck without this one instance". For upgrade-after pass probeUpgrade=1, removeUpgrade=0.
    public static CardEvResult? RequestCardEv(string probeModelId, int probeUpgrade, int? removeUpgrade = null)
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

            CardEvResult result = EnqueueCard(snapshot, probeModelId, probeUpgrade, removeUpgrade);
            EnsureWorker();
            return result;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"RealtimeEvService.RequestCardEv failed: {ex.Message}", 0);
            return null;
        }
    }

    private static CardEvResult EnqueueCard(DeckSnapshot snapshot, string cardId, int upgrade, int? removeUpgrade)
    {
        // The mode is part of the key: the same card has a different value as "add to deck" vs.
        // "already in deck" (remove-one baseline), so they must not share a cached result.
        string modeTag = removeUpgrade is int ru ? $"|rm{ru}" : "|add";
        string resultKey = $"{snapshot.Signature}|{cardId}+{upgrade}{modeTag}";
        CardEvResult result = results.GetOrAdd(resultKey, _ => new CardEvResult());

        // (Re)queue only when the result is not settled AND no live work is already queued/computing
        // for it. inFlight makes this self-healing: if the previous work item was dropped (signature
        // skip) or lost, the key is no longer in inFlight, so a later request re-queues it. This is
        // what lets the overlay recover from any transient desync instead of getting stuck on "...".
        if (!result.IsSettled && inFlight.TryAdd(resultKey, 0))
        {
            queue.Enqueue(new WorkItem(resultKey, snapshot.Signature, snapshot.Layer, snapshot.Cards, cardId, upgrade, removeUpgrade));
        }

        return result;
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
                    // Deck cards are already owned -> in-deck basis (value = with vs. without this one),
                    // matching what the deck-view render requests, so the precompute is reused.
                    EnqueueCard(snapshot, card.Id, card.Upgrade, removeUpgrade: card.Upgrade);
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
    /// for now - call this with the event's offered cards once event detection is added.
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
                // Event-offered cards are not owned yet -> add basis (like the reward screen).
                EnqueueCard(snapshot, id, upgrade, removeUpgrade: null);
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

        // Dedicated BelowNormal-priority background thread (NOT a thread-pool Task, which would run at
        // normal priority and compete with the game as an equal). During combat the simulator runs its
        // serial path inline on THIS thread, so the whole fight-time compute inherits BelowNormal and the
        // OS always lets the game's threads preempt it - no stutter.
        Thread worker = new(() =>
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
                // Guarantee the last batch is persisted even if the throttle skipped it mid-drain.
                if (cacheDirty)
                {
                    cacheDirty = false;
                    lastCacheSaveTick = Environment.TickCount64;
                    SaveCacheToDisk();
                }

                Interlocked.Exchange(ref workerRunning, 0);
                // A late enqueue between drain end and flag reset: restart if needed.
                if (!queue.IsEmpty)
                {
                    EnsureWorker();
                }
            }
        })
        {
            IsBackground = true,
            Priority = ThreadPriority.BelowNormal,
            Name = "CardValueOverlay-EvWorker"
        };
        worker.Start();
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

                inFlight.TryRemove(item.ResultKey, out _);
            }

            return;
        }

        // Runs continuously (during combat too, but on few cores via CurrentRunDegree so it's
        // imperceptible). Stale items - for a deck/floor the UI has already moved past - are skipped;
        // clearing inFlight lets a later request for the current signature re-queue the work.
        int computed = 0;
        int skipped = 0;
        while (queue.TryDequeue(out WorkItem? item))
        {
            if (!string.Equals(item.DeckSignature, currentSignature, StringComparison.Ordinal))
            {
                inFlight.TryRemove(item.ResultKey, out _);
                skipped++;
                continue;
            }

            ComputeOne(item);
            inFlight.TryRemove(item.ResultKey, out _);
            computed++;
        }

        if (computed > 0 || skipped > 0)
        {
            MainFile.Logger.Info($"RealtimeEvService drain: computed={computed} skipped={skipped} currentSig={currentSignature}", 0);
        }

        TrimInMemoryCaches();

        // Persist the batch's results so a later game session reuses them (throttled write).
        MaybeSaveCache();
    }

    // Bounds the in-memory dictionaries. When over the cap, drop entries for decks other than the
    // one the UI is currently looking at (their signatures embed the whole deck, so old-floor/other-
    // deck entries are never reused); the current deck's entries are always kept. Worst case on a
    // revisit is a recompute - never incorrectness.
    private static void TrimInMemoryCaches()
    {
        if (results.Count <= MaxInMemoryEntries && baselineByDeck.Count <= MaxInMemoryEntries)
        {
            return;
        }

        string keepPrefix = currentSignature + "|";
        foreach (string key in results.Keys)
        {
            if (!key.StartsWith(keepPrefix, StringComparison.Ordinal))
            {
                results.TryRemove(key, out _);
                inFlight.TryRemove(key, out _);
            }
        }

        foreach (string key in baselineByDeck.Keys)
        {
            if (!string.Equals(key, currentSignature, StringComparison.Ordinal))
            {
                baselineByDeck.TryRemove(key, out _);
            }
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
            long computeStartTick = Environment.TickCount64;
            string mode = item.RemoveUpgrade is int rmu ? $"inDeck-rm{rmu}" : "add";
            MainFile.Logger.Info($"[EV] start {DateTime.Now:HH:mm:ss.fff} card={item.ProbeId}+{item.ProbeUpgrade} mode={mode} degree={CurrentRunDegree()} inCombat={inCombat} qDepth={queue.Count}", 0);

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

            SimulationCard? probeBase = MapCard(item.ProbeId!, item.ProbeUpgrade, lib);
            if (probeBase is null)
            {
                result.Failed = true;
                return;
            }

            // Wrap the probe with a UNIQUE modelId so block + tracking (both keyed by modelId) affect
            // ONLY this one probe instance, never same-name cards already in the deck. All behaviour
            // fields are copied verbatim, so the simulator plays it identically to the real card.
            string probeId = ProbeModelIdPrefix + probeBase.ModelId;
            SimulationCard probe = probeBase with { ModelId = probeId };

            // Modest cores during combat (BelowNormal priority so the game keeps its core), many otherwise.
            int runDegree = CurrentRunDegree();

            // baselineDeck = the deck WITHOUT this card. For the in-deck basis (RemoveUpgrade set) the card
            // is already owned, so remove exactly ONE matching instance; for the add basis (reward) the
            // deck already lacks it, so use the deck as-is. normalDeck = baselineDeck + the probe, so:
            //   add:     baseline = current deck,           normal = deck + card   (value of ADDING it)
            //   in-deck: baseline = deck minus this 1 card, normal = full deck      (value of HAVING it)
            List<SimulationCard> baselineDeck = deck;
            string baselineKey = item.DeckSignature;
            if (item.RemoveUpgrade is int removeUpgrade)
            {
                baselineDeck = [.. deck];
                int idx = baselineDeck.FindIndex(c =>
                    string.Equals(c.ModelId, item.ProbeId, StringComparison.Ordinal) && c.UpgradeLevel == removeUpgrade);
                if (idx >= 0)
                {
                    baselineDeck.RemoveAt(idx);
                }

                baselineKey = $"{item.DeckSignature}|-{item.ProbeId}+{removeUpgrade}";
            }

            List<SimulationCard> normalDeck = [.. baselineDeck, probe];
            DeckMonteCarloSimulator simulator = new();
            int maxTurns = Horizons[^1];

            // Single pass over the full (14-turn) horizon: short/mid are prefix sums of the SAME runs,
            // so they cost nothing extra beyond the long horizon. baseline/normal/blocked all share the
            // fixed seed => common random numbers, so the per-play delta and dEV stay paired.
            double[] baselineByTurn = baselineByDeck.GetOrAdd(
                baselineKey,
                _ => SimulatePerTurn(baselineDeck, maxTurns, lib, runDegree));

            TrackedCardSimulationReport normal = simulator.SimulateTrackedCard(
                normalDeck, BuildOptions(maxTurns, lib, runDegree, blockedProbeId: null), probeId, collectCredits: false);
            result.ProgressStage = 1;
            TrackedCardSimulationReport blocked = simulator.SimulateTrackedCard(
                normalDeck, BuildOptions(maxTurns, lib, runDegree, blockedProbeId: probeId), probeId, collectCredits: false);
            result.ProgressStage = 2;

            for (int i = 0; i < Horizons.Length; i++)
            {
                WriteHorizon(result, i, Horizons[i], normal, blocked, baselineByTurn);
            }

            // Written LAST: publishes the value writes above to readers and signals "done" even when
            // every calc value is null (unplayable / 0 plays). The overlay stops waiting on this.
            result.ProgressStage = 3;
            result.Complete = true;
            cacheDirty = true;
            MainFile.Logger.Info($"[EV] done  {DateTime.Now:HH:mm:ss.fff} card={item.ProbeId}+{item.ProbeUpgrade} mode={mode} degree={runDegree} runs={RunsPerSim} elapsed={Environment.TickCount64 - computeStartTick}ms calc s/m/l={result.CalcShort:0.#}/{result.CalcMid:0.#}/{result.CalcLong:0.#}", 0);
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
            cacheDirty = true;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"RealtimeEvService.ComputeBaselineOnly failed: {ex.Message}", 0);
        }
    }

    // Fills one horizon's four result columns from the (single-pass) reports. valuePerPlay =
    // per-run delta x RunsPerSim / total-plays (ExpectedValue is a per-run mean, PlayCount a total
    // across all runs), so the multiplier is exactly the run count of the simulation.
    private static void WriteHorizon(
        CardEvResult result,
        int index,
        int turns,
        TrackedCardSimulationReport normal,
        TrackedCardSimulationReport blocked,
        double[] baselinePerTurn)
    {
        decimal normalEv = SumExpectedValue(normal, turns);
        decimal blockedEv = SumExpectedValue(blocked, turns);
        int plays = SumPlayCount(normal, turns);
        double baseline = SumBaseline(baselinePerTurn, turns);

        double? valuePerPlay = plays == 0
            ? null
            : (double)((normalEv - blockedEv) * RunsPerSim / plays);
        double after = (double)normalEv;
        double deltaEv = after - baseline;

        switch (index)
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
            // so the per-play delta and dEV are far less noisy than the absolute EVs.
            Seed = SimulationSeed,
            HandSize = HandSize,
            MaxHandSize = MaxHandSize,
            BaseEnergy = BaseEnergy,
            BaseStars = BaseStars,
            StarsPersistBetweenTurns = true,
            MaxCardsPlayedPerTurn = MaxCardsPlayedPerTurn,
            MaxBranchingCards = MaxBranch,
            CardLibrary = lib.Library,
            GeneratedCardPools = generatedPools,
            BlockedPlayModelIds = blockedProbeId is null ? [] : [blockedProbeId],
            // During combat the parallel workers run BelowNormal so the OS lets the game preempt them;
            // outside combat Normal restores any thread lowered during a previous fight.
            WorkerThreadPriority = inCombat ? ThreadPriority.BelowNormal : ThreadPriority.Normal
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
                    setupValues: cardSetupValues);
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
                cardSetupValues = CardSetupValueCatalog.Parse(
                    ReadResource("card_setup_values.json"), options);

                dataLoaded = true;
                LoadCacheFromDisk();
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

    // On-disk shape of the calc cache. Results are 12-element arrays [calc s/m/l, delta s/m/l,
    // baseline s/m/l, after s/m/l] (nullable because calc can legitimately be null for 0-play cards).
    private sealed class CacheFile
    {
        public int SchemaVersion { get; set; }
        public string ComputeKey { get; set; } = "";
        public Dictionary<string, double[]> Baselines { get; set; } = new();
        public Dictionary<string, double?[]> Results { get; set; } = new();
    }

    private static void LoadCacheFromDisk()
    {
        try
        {
            if (!GodotFileAccess.FileExists(CacheFilePath))
            {
                return;
            }

            using GodotFileAccess file = GodotFileAccess.Open(CacheFilePath, GodotFileAccess.ModeFlags.Read);
            if (file is null)
            {
                return;
            }

            CacheFile? data = JsonSerializer.Deserialize<CacheFile>(file.GetAsText());
            if (data is null || !string.Equals(data.ComputeKey, CacheComputeKey, StringComparison.Ordinal))
            {
                // Missing or built with different params/semantics: ignore so stale values never show.
                return;
            }

            foreach (KeyValuePair<string, double[]> kv in data.Baselines)
            {
                baselineByDeck.TryAdd(kv.Key, kv.Value);
            }

            int loaded = 0;
            foreach (KeyValuePair<string, double?[]> kv in data.Results)
            {
                double?[] a = kv.Value;
                if (a.Length < 12)
                {
                    continue;
                }

                results.TryAdd(kv.Key, new CardEvResult
                {
                    CalcShort = a[0], CalcMid = a[1], CalcLong = a[2],
                    DeltaShort = a[3], DeltaMid = a[4], DeltaLong = a[5],
                    BaselineShort = a[6], BaselineMid = a[7], BaselineLong = a[8],
                    AfterShort = a[9], AfterMid = a[10], AfterLong = a[11],
                    ProgressStage = 3,
                    Complete = true
                });
                loaded++;
            }

            MainFile.Logger.Info($"RealtimeEvService: loaded {loaded} cached results, {data.Baselines.Count} baselines.", 0);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"RealtimeEvService: failed to load calc cache: {ex.Message}", 0);
        }
    }

    // Called after a drain batch (background thread). Throttled so a burst of computes writes once.
    private static void MaybeSaveCache()
    {
        if (!cacheDirty)
        {
            return;
        }

        long now = Environment.TickCount64;
        if (now - lastCacheSaveTick < CacheSaveThrottleMs)
        {
            return;
        }

        cacheDirty = false;
        lastCacheSaveTick = now;
        SaveCacheToDisk();
    }

    private static void SaveCacheToDisk()
    {
        try
        {
            CacheFile data = new() { SchemaVersion = 1, ComputeKey = CacheComputeKey };

            foreach (KeyValuePair<string, double[]> kv in baselineByDeck)
            {
                if (data.Baselines.Count >= MaxPersistedEntries)
                {
                    break;
                }

                data.Baselines[kv.Key] = kv.Value;
            }

            foreach (KeyValuePair<string, CardEvResult> kv in results)
            {
                if (data.Results.Count >= MaxPersistedEntries)
                {
                    break;
                }

                CardEvResult r = kv.Value;
                if (!r.Complete || r.Failed)
                {
                    continue; // only persist settled successes
                }

                data.Results[kv.Key] =
                [
                    r.CalcShort, r.CalcMid, r.CalcLong,
                    r.DeltaShort, r.DeltaMid, r.DeltaLong,
                    r.BaselineShort, r.BaselineMid, r.BaselineLong,
                    r.AfterShort, r.AfterMid, r.AfterLong
                ];
            }

            using GodotFileAccess file = GodotFileAccess.Open(CacheFilePath, GodotFileAccess.ModeFlags.Write);
            if (file is null)
            {
                return;
            }

            file.StoreString(JsonSerializer.Serialize(data));
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"RealtimeEvService: failed to save calc cache: {ex.Message}", 0);
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
                return floor > 0 ? floor : DefaultLayerFallback;
            }
            catch
            {
                return DefaultLayerFallback;
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
