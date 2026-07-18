using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CardValueOverlay.CardValueOverlayCode.Configuration;
using CardValueOverlay.Core.Analysis;
using CardValueOverlay.Core.Configuration;
using CardValueOverlay.Modeling.Estimation;
using CardValueOverlay.Modeling.Extraction;
using CardValueOverlay.Modeling.Simulation;
using GodotFileAccess = Godot.FileAccess;

namespace CardValueOverlay.CardValueOverlayCode.Runtime;

/// <summary>
/// Runs the Monte Carlo deck simulator in-game on a background thread to compute the
/// deck-contextual dEV of adding, owning, or upgrading a card. Everything is wrapped so a
/// failure degrades to "no value" and never crashes or blocks the game. Results publish at
/// paired 15-run checkpoints and refine each horizon independently through the configured maximum.
/// </summary>
public static class RealtimeEvService
{
    // Short / mid / long are independent search problems. Each horizon builds its own sample stream
    // with its own Turns setting; no value is taken from a longer policy's trajectory prefix.
    private static readonly int[] Horizons =
    [
        TrainingHorizonTurnCounts.Shortline,
        TrainingHorizonTurnCounts.Midline,
        TrainingHorizonTurnCounts.Longline
    ];
    private const int HandSize = 5;
    private const int MaxHandSize = 10;
    private const int BaseEnergy = 3;
    private const int BaseStars = 3;
    private const int ResolvedPlaySafetyCap = DeckSimulationOptions.DefaultResolvedPlaySafetyCap;
    private const int SimulationSeed = 20260705; // fixed so results are deterministic + cache-comparable
    private const int DefaultLayerFallback = 17; // Act 2/3 pressure band, used when TotalFloor is unavailable

    public sealed class CardEvResult
    {
        public volatile HorizonDeltaResult? Short;
        public volatile HorizonDeltaResult? Mid;
        public volatile HorizonDeltaResult? Long;

        public int MaxRuns;

        public volatile bool Failed;

        public volatile bool Complete;

        public bool IsSettled => Complete || Failed;

        public double ProgressFraction => IsSettled
            ? 1d
            : MaxRuns <= 0
                ? 0d
                : Math.Clamp(
                    ((Short?.CompletedRuns ?? 0)
                        + (Mid?.CompletedRuns ?? 0)
                        + (Long?.CompletedRuns ?? 0))
                        / (3d * MaxRuns),
                    0d,
                    1d);
    }

    public sealed record HorizonDeltaResult(
        double Mean,
        double LowerConfidence,
        double UpperConfidence,
        double LowerStopping,
        double UpperStopping,
        int CompletedRuns,
        SamplingState State)
    {
        public bool HasStableSign => LowerStopping > 0d || UpperStopping < 0d;

        public bool Complete => State is SamplingState.Stable or SamplingState.MaxUncertain;
    }

    public enum SamplingState
    {
        Preview,
        Refining,
        Stable,
        MaxUncertain
    }

    private enum CardEvBasis
    {
        Add,
        Owned,
        Replace
    }

    private sealed class SimulationSampleSeries
    {
        public List<double> TotalValuesByRun { get; set; } = [];

        public bool IsValid => TotalValuesByRun.All(double.IsFinite);
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
    // Every distinct before/after deck and horizon stores its own per-run total. A current-deck series
    // is shared by visible reward candidates only when both the deck and independently solved horizon
    // match; remove-one and replacement variants use their own keys.
    private static readonly ConcurrentDictionary<string, SimulationSampleSeries> samplesBySimulation = new();
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
    private const string CacheFilePath = "user://CardValueOverlay_dev_cache.json";
    private const int MaxPersistedEntries = 8000;
    // Hard cap on the in-memory result/baseline dictionaries so a very long / multi-run session
    // can't grow them unbounded (keys embed the whole-deck signature, so they never get reused).
    private const int MaxInMemoryEntries = 4000;
    private const long CacheSaveThrottleMs = 1500;
    private static string CacheComputeKey =>
        $"v3|{CardValueOverlayModConfig.CurrentSettings.CacheKey}|batch15|pairedT|independentHorizons|stopBonferroni|stableShuffle1|resolvedCap{ResolvedPlaySafetyCap}|loop1|forcedPrelude1|selectiveGap{RealtimeSearchBranchPolicy.SelectiveThirdBranchMinScoreGap}|nodeBudget4-250000_8-60000_12-100000|slices4-4_8-2_12-1_combat1|h{string.Join('-', Horizons)}|seed{SimulationSeed}|sem14";
    private static volatile bool cacheDirty;
    private static long lastCacheSaveTick;

    // Background compute remains processor-count-aware but deliberately conservative. Combat state
    // selects the smaller policy; reward, upgrade, deck, map, and event screens use the non-combat
    // policy. Both are capped and run BelowNormal so simulation cannot occupy most of the machine.
    private static volatile bool inCombat;

    // The signature of the deck/floor the UI currently cares about. Work queued for a different
    // signature (e.g. computed for floor 14 but you already moved to floor 16) is stale and skipped.
    private static volatile string currentSignature = "";

    // Non-combat work scales by horizon (4/2/1 workers for 4/8/12 turns on this machine). Combat
    // work is always serial and advances one deterministic run per queue slice. Combat is bounded
    // by SetUpCombat and CombatEnded.
    private static int CurrentRunDegree(int turns) =>
        RealtimeWorkerPolicy.ResolveRunDegree(Environment.ProcessorCount, inCombat, turns);

    private static int CurrentRunsPerSlice(int turns) =>
        RealtimeWorkerPolicy.ResolveRunsPerSlice(Environment.ProcessorCount, inCombat, turns);

    public static void OnCombatStart()
    {
        inCombat = true;
        EnsureCombatEndedSubscription();
        // Use the (BelowNormal, imperceptible) combat time to precompute the deck's baseline EV, so the
        // card-reward screen that follows fills in fast.
        try
        {
            DeckSnapshot? snapshot = GetSnapshot();
            string? baselineKey = snapshot is null ? null : $"baseline|{snapshot.Signature}";
            if (snapshot is not null
                && Horizons.Any(horizon =>
                    SampleCount(snapshot.Signature, horizon) < RealtimeSimulationSettings.RunBatchSize)
                && inFlight.TryAdd(baselineKey!, 0))
            {
                queue.Enqueue(new WorkItem(
                    baselineKey!,
                    snapshot.Signature,
                    snapshot.Layer,
                    snapshot.Cards,
                    null,
                    0,
                    null,
                    null,
                    snapshot.Settings,
                    CardEvBasis.Add));
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
        // Combat over: the worker switches to the capped non-combat policy. Any leftover stale work
        // is skipped once the next overlay render updates currentSignature.
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

    public static void OnSimulationSettingsChanged()
    {
        lock (snapshotLock)
        {
            cachedSnapshot = null;
            cachedSnapshotTick = 0;
        }

        currentSignature = "";
        while (queue.TryDequeue(out WorkItem? item))
        {
            inFlight.TryRemove(item.ResultKey, out _);
        }

        RealtimeSimulationSettings settings = CardValueOverlayModConfig.CurrentSettings;
        MainFile.Logger.Info(
            $"Realtime simulation settings changed: branch={settings.Branch}, depth={settings.TurnDepth}, " +
            $"minRuns={settings.MinRuns}, maxRuns={settings.MaxRuns}, complexMinRuns={settings.ComplexCardMinRuns}, " +
            $"confidence={settings.ConfidenceLevelPercent}, earlyStop={settings.EarlyStoppingEnabled}.",
            0);
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
        CardEnchantmentRef? ProbeEnchantment,
        int? RemoveUpgrade,
        RealtimeSimulationSettings Settings,
        CardEvBasis Basis);

    /// <summary>
    /// Returns the current (possibly still-computing) EV result for adding this card to the
    /// live deck, queueing a background computation if none exists yet. Returns null when no
    /// run is active or the live deck can't be read. Never throws.
    /// </summary>
    // removeUpgrade: null = ADD the card to the current deck (reward screen, card not yet owned).
    // Non-null = the card is already in the deck (deck view / upgrade preview): value it as "in deck
    // vs. deck without this one instance". For upgrade-after pass probeUpgrade=1, removeUpgrade=0.
    public static CardEvResult? RequestCardEv(
        string probeModelId,
        int probeUpgrade,
        int? removeUpgrade = null,
        CardEnchantmentRef? enchantment = null)
    {
        return RequestCardEvCore(
            probeModelId,
            probeUpgrade,
            enchantment,
            removeUpgrade,
            removeUpgrade is null ? CardEvBasis.Add : CardEvBasis.Owned);
    }

    public static CardEvResult? RequestUpgradeEv(
        string probeModelId,
        CardEnchantmentRef? enchantment = null)
    {
        return RequestCardEvCore(
            probeModelId,
            probeUpgrade: 1,
            enchantment,
            removeUpgrade: 0,
            basis: CardEvBasis.Replace);
    }

    private static CardEvResult? RequestCardEvCore(
        string probeModelId,
        int probeUpgrade,
        CardEnchantmentRef? enchantment,
        int? removeUpgrade,
        CardEvBasis basis)
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

            CardEvResult result = EnqueueCard(
                snapshot,
                probeModelId,
                probeUpgrade,
                enchantment,
                removeUpgrade,
                basis);
            EnsureWorker();
            return result;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"RealtimeEvService request failed ({basis}): {ex.Message}", 0);
            return null;
        }
    }

    private static CardEvResult EnqueueCard(
        DeckSnapshot snapshot,
        string cardId,
        int upgrade,
        CardEnchantmentRef? enchantment,
        int? removeUpgrade,
        CardEvBasis basis)
    {
        string cardToken = CardToken(cardId, upgrade, enchantment);
        string modeTag = basis switch
        {
            CardEvBasis.Add => "|add",
            CardEvBasis.Owned => $"|owned-without:{CardToken(cardId, removeUpgrade ?? upgrade, enchantment)}",
            CardEvBasis.Replace => $"|replace:{CardToken(cardId, removeUpgrade ?? 0, enchantment)}->{cardToken}",
            _ => throw new ArgumentOutOfRangeException(nameof(basis))
        };
        string resultKey = $"{snapshot.Signature}|{cardToken}{modeTag}";
        CardEvResult result = results.GetOrAdd(resultKey, _ => new CardEvResult
        {
            MaxRuns = snapshot.Settings.MaxRuns
        });

        // (Re)queue only when the result is not settled AND no live work is already queued/computing
        // for it. inFlight makes this self-healing: if the previous work item was dropped (signature
        // skip) or lost, the key is no longer in inFlight, so a later request re-queues it. This is
        // what lets the overlay recover from any transient desync instead of getting stuck on "...".
        if (!result.IsSettled && inFlight.TryAdd(resultKey, 0))
        {
            queue.Enqueue(new WorkItem(
                resultKey,
                snapshot.Signature,
                snapshot.Layer,
                snapshot.Cards,
                cardId,
                upgrade,
                enchantment,
                removeUpgrade,
                snapshot.Settings,
                basis));
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
                CardEnchantmentRef? enchantment = EnchantmentOf(card);
                if (seen.Add(CardToken(card.Id, card.Upgrade, enchantment)))
                {
                    // Deck cards are already owned -> in-deck basis (value = with vs. without this one),
                    // matching what the deck-view render requests, so the precompute is reused.
                    EnqueueCard(
                        snapshot,
                        card.Id,
                        card.Upgrade,
                        enchantment,
                        removeUpgrade: card.Upgrade,
                        basis: CardEvBasis.Owned);
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
                EnqueueCard(
                    snapshot,
                    id,
                    upgrade,
                    enchantment: null,
                    removeUpgrade: null,
                    basis: CardEvBasis.Add);
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
                    FailResult(r);
                }

                inFlight.TryRemove(item.ResultKey, out _);
            }

            return;
        }

        // Runs continuously under the capped background policy. Stale items - for a deck/floor the
        // UI has already moved past - are skipped;
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

            bool reschedule = ComputeOne(item);
            if (reschedule)
            {
                queue.Enqueue(item);
            }
            else
            {
                inFlight.TryRemove(item.ResultKey, out _);
            }
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
        if (results.Count <= MaxInMemoryEntries &&
            samplesBySimulation.Count <= MaxInMemoryEntries)
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

        foreach (string key in samplesBySimulation.Keys)
        {
            if (!string.Equals(key, currentSignature, StringComparison.Ordinal) &&
                !key.StartsWith(keepPrefix, StringComparison.Ordinal))
            {
                samplesBySimulation.TryRemove(key, out _);
            }
        }
    }

    private sealed record MappedDeck(
        List<SimulationCard> Cards,
        List<int> StableIds);

    private sealed record CounterfactualPair(
        string BeforeKey,
        MappedDeck Before,
        string AfterKey,
        MappedDeck After,
        SimulationCard Probe);

    // Returns true when another horizon/batch is required. The queue places that continuation at
    // the tail, so visible cards advance through 4/8/12 previews before one card is refined deeply.
    private static bool ComputeOne(WorkItem item)
    {
        if (item.ProbeId is null)
        {
            return ComputeBaselineOnly(item);
        }

        if (!results.TryGetValue(item.ResultKey, out CardEvResult? result))
        {
            return false;
        }

        try
        {
            long computeStartTick = Environment.TickCount64;
            string probeToken = CardToken(item.ProbeId, item.ProbeUpgrade, item.ProbeEnchantment);
            (int HorizonIndex, int Turns, HorizonDeltaResult? Current)? nextHorizon =
                SelectNextHorizon(result);
            if (nextHorizon is null)
            {
                result.Complete = true;
                return false;
            }

            (int horizonIndex, int turns, HorizonDeltaResult? currentHorizon) = nextHorizon.Value;
            int completedRuns = currentHorizon?.CompletedRuns ?? 0;
            MainFile.Logger.Info(
                $"[dEV] start {DateTime.Now:HH:mm:ss.fff} card={probeToken} basis={item.Basis} " +
                $"horizon={turns} completed={completedRuns} max={item.Settings.MaxRuns} " +
                $"degree={CurrentRunDegree(turns)} qDepth={queue.Count}",
                0);

            LibraryForLayer? lib = GetLibrary(item.Layer);
            if (lib is null)
            {
                FailResult(result);
                return false;
            }

            MappedDeck currentDeck = MapDeckWithStableIds(item.DeckCards, lib);
            if (currentDeck.Cards.Count == 0)
            {
                FailResult(result);
                return false;
            }

            SimulationCard? probe = MapCard(
                item.ProbeId,
                item.ProbeUpgrade,
                lib,
                item.ProbeEnchantment);
            if (probe is null)
            {
                FailResult(result);
                return false;
            }

            CounterfactualPair pair = BuildCounterfactualPair(item, currentDeck, probe);
            int targetRuns = Math.Min(
                item.Settings.MaxRuns,
                Math.Max(
                    RealtimeSimulationSettings.RunBatchSize,
                    completedRuns + RealtimeSimulationSettings.RunBatchSize));
            int runDegree = CurrentRunDegree(turns);
            int runsPerSlice = CurrentRunsPerSlice(turns);

            SimulationSampleSeries before = EnsureSamples(
                pair.BeforeKey,
                pair.Before,
                turns,
                targetRuns,
                lib,
                runDegree,
                item.Settings,
                maximumRunsToAdd: 0);
            SimulationSampleSeries after = EnsureSamples(
                pair.AfterKey,
                pair.After,
                turns,
                targetRuns,
                lib,
                runDegree,
                item.Settings,
                maximumRunsToAdd: 0);

            if (before.TotalValuesByRun.Count < targetRuns
                || after.TotalValuesByRun.Count < targetRuns)
            {
                bool advanceBefore = before.TotalValuesByRun.Count < targetRuns
                    && (before.TotalValuesByRun.Count <= after.TotalValuesByRun.Count
                        || after.TotalValuesByRun.Count >= targetRuns);
                if (advanceBefore)
                {
                    before = EnsureSamples(
                        pair.BeforeKey,
                        pair.Before,
                        turns,
                        targetRuns,
                        lib,
                        runDegree,
                        item.Settings,
                        runsPerSlice);
                }
                else
                {
                    after = EnsureSamples(
                        pair.AfterKey,
                        pair.After,
                        turns,
                        targetRuns,
                        lib,
                        runDegree,
                        item.Settings,
                        runsPerSlice);
                }

                cacheDirty = true;
                if (!string.Equals(item.DeckSignature, currentSignature, StringComparison.Ordinal))
                {
                    return false;
                }

                if (before.TotalValuesByRun.Count < targetRuns
                    || after.TotalValuesByRun.Count < targetRuns)
                {
                    return true;
                }
            }

            int pairedRuns = Math.Min(before.TotalValuesByRun.Count, after.TotalValuesByRun.Count);
            pairedRuns = Math.Min(pairedRuns, targetRuns);
            if (pairedRuns is not (15 or 30 or 45 or 60))
            {
                throw new InvalidOperationException($"Unexpected paired run count {pairedRuns}.");
            }

            bool complexProbe = IsComplexRealtimeProbe(pair.Probe);
            int minimumRuns = item.Settings.EffectiveMinimumRuns(complexProbe);
            int plannedStoppingLooks = item.Settings.EarlyStoppingEnabled
                ? item.Settings.PlannedStoppingLooks(complexProbe)
                : 1;
            HorizonDeltaResult horizon = BuildPairedHorizonResult(
                before,
                after,
                pairedRuns,
                item.Settings.ConfidenceLevelPercent,
                plannedStoppingLooks,
                minimumRuns,
                item.Settings.MaxRuns,
                item.Settings.EarlyStoppingEnabled);
            SetHorizon(result, horizonIndex, horizon);
            result.Complete = result.Short?.Complete == true
                && result.Mid?.Complete == true
                && result.Long?.Complete == true;
            cacheDirty = true;

            MainFile.Logger.Info(
                $"[dEV] batch card={probeToken} basis={item.Basis} horizon={turns} " +
                $"runs={pairedRuns}/{item.Settings.MaxRuns} state={horizon.State} " +
                $"elapsed={Environment.TickCount64 - computeStartTick}ms mean={horizon.Mean:0.#}",
                0);
            return !result.Complete;
        }
        catch (Exception ex)
        {
            FailResult(result);
            MainFile.Logger.Warn($"RealtimeEvService compute failed for {item.ProbeId}: {ex}", 0);
            return false;
        }
    }

    private static void FailResult(CardEvResult result)
    {
        result.Failed = true;
    }

    private static (int HorizonIndex, int Turns, HorizonDeltaResult? Current)? SelectNextHorizon(
        CardEvResult result)
    {
        HorizonDeltaResult?[] current = [result.Short, result.Mid, result.Long];
        int selected = -1;
        int fewestRuns = int.MaxValue;
        for (int index = 0; index < current.Length; index++)
        {
            HorizonDeltaResult? horizon = current[index];
            if (horizon?.Complete == true)
            {
                continue;
            }

            int runs = horizon?.CompletedRuns ?? 0;
            if (runs < fewestRuns)
            {
                selected = index;
                fewestRuns = runs;
            }
        }

        return selected < 0 ? null : (selected, Horizons[selected], current[selected]);
    }

    private static void SetHorizon(CardEvResult result, int horizonIndex, HorizonDeltaResult horizon)
    {
        switch (horizonIndex)
        {
            case 0:
                result.Short = horizon;
                break;
            case 1:
                result.Mid = horizon;
                break;
            case 2:
                result.Long = horizon;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(horizonIndex));
        }
    }

    private static CounterfactualPair BuildCounterfactualPair(
        WorkItem item,
        MappedDeck current,
        SimulationCard probe)
    {
        string probeToken = CardToken(item.ProbeId, item.ProbeUpgrade, item.ProbeEnchantment);
        if (item.Basis == CardEvBasis.Add)
        {
            MappedDeck withProbe = CloneDeck(current);
            withProbe.Cards.Add(probe);
            withProbe.StableIds.Add(NextStableId(current.StableIds));
            return new CounterfactualPair(
                item.DeckSignature,
                current,
                $"{item.DeckSignature}|add:{probeToken}",
                withProbe,
                probe);
        }

        int removeUpgrade = item.RemoveUpgrade
            ?? throw new InvalidOperationException($"{item.Basis} requires a remove-upgrade form.");
        MappedDeck without = CloneDeck(current);
        int removeIndex = without.Cards.FindIndex(card =>
            string.Equals(card.ModelId, item.ProbeId, StringComparison.Ordinal)
            && card.UpgradeLevel == removeUpgrade
            && MatchesEnchantment(card.Enchantment, item.ProbeEnchantment));
        if (removeIndex < 0)
        {
            throw new InvalidOperationException(
                $"Could not remove {CardToken(item.ProbeId, removeUpgrade, item.ProbeEnchantment)} from the current deck.");
        }

        int removedStableId = without.StableIds[removeIndex];
        without.Cards.RemoveAt(removeIndex);
        without.StableIds.RemoveAt(removeIndex);
        string removedToken = CardToken(item.ProbeId, removeUpgrade, item.ProbeEnchantment);
        string withoutKey = $"{item.DeckSignature}|without:{removedToken}";

        if (item.Basis == CardEvBasis.Owned)
        {
            bool restoresCurrentForm = item.ProbeUpgrade == removeUpgrade;
            if (restoresCurrentForm)
            {
                return new CounterfactualPair(
                    withoutKey,
                    without,
                    item.DeckSignature,
                    current,
                    probe);
            }

            MappedDeck alternateForm = CloneDeck(without);
            alternateForm.Cards.Add(probe);
            alternateForm.StableIds.Add(removedStableId);
            return new CounterfactualPair(
                withoutKey,
                without,
                $"{withoutKey}|form:{probeToken}",
                alternateForm,
                probe);
        }

        MappedDeck replacement = CloneDeck(without);
        replacement.Cards.Add(probe);
        replacement.StableIds.Add(removedStableId);
        return new CounterfactualPair(
            item.DeckSignature,
            current,
            $"{item.DeckSignature}|replace:{removedToken}->{probeToken}",
            replacement,
            probe);
    }

    private static MappedDeck CloneDeck(MappedDeck deck)
    {
        return new MappedDeck([.. deck.Cards], [.. deck.StableIds]);
    }

    private static int NextStableId(IReadOnlyList<int> stableIds)
    {
        return stableIds.Count == 0 ? 0 : stableIds.Max() + 1;
    }

    private static bool ComputeBaselineOnly(WorkItem item)
    {
        try
        {
            LibraryForLayer? lib = GetLibrary(item.Layer);
            if (lib is null)
            {
                return false;
            }

            MappedDeck deck = MapDeckWithStableIds(item.DeckCards, lib);
            if (deck.Cards.Count == 0)
            {
                return false;
            }

            foreach (int horizon in Horizons)
            {
                if (SampleCount(item.DeckSignature, horizon)
                    >= RealtimeSimulationSettings.RunBatchSize)
                {
                    continue;
                }

                EnsureSamples(
                    item.DeckSignature,
                    deck,
                    horizon,
                    RealtimeSimulationSettings.RunBatchSize,
                    lib,
                    CurrentRunDegree(horizon),
                    item.Settings,
                    CurrentRunsPerSlice(horizon));
                cacheDirty = true;
                if (!string.Equals(item.DeckSignature, currentSignature, StringComparison.Ordinal))
                {
                    return false;
                }

                return Horizons.Any(candidate =>
                    SampleCount(item.DeckSignature, candidate)
                    < RealtimeSimulationSettings.RunBatchSize);
            }

            return false;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"RealtimeEvService.ComputeBaselineOnly failed: {ex}", 0);
            return false;
        }
    }

    private static SimulationSampleSeries EnsureSamples(
        string simulationKey,
        MappedDeck deck,
        int horizon,
        int targetRuns,
        LibraryForLayer lib,
        int runDegree,
        RealtimeSimulationSettings settings,
        int maximumRunsToAdd)
    {
        string horizonKey = SampleSeriesKey(simulationKey, horizon);
        SimulationSampleSeries series = samplesBySimulation.GetOrAdd(
            horizonKey,
            _ => new SimulationSampleSeries());
        if (!series.IsValid)
        {
            throw new InvalidOperationException($"Invalid cached sample series for {horizonKey}.");
        }

        int startRun = series.TotalValuesByRun.Count;
        int missing = Math.Min(
            targetRuns - startRun,
            Math.Max(0, maximumRunsToAdd));
        if (missing <= 0)
        {
            return series;
        }

        SearchBudgetTelemetryCollector budgetTelemetry = new();
        DeckSimulationOptions options = BuildOptions(
            horizon,
            lib,
            runDegree,
            settings,
            deck.StableIds,
            budgetTelemetry);
        long wallStartedAt = Stopwatch.GetTimestamp();
        double cpuStartedMs = ReadProcessCpuMilliseconds();
        long allocatedStarted = GC.GetTotalAllocatedBytes(precise: false);
        int gen0Started = GC.CollectionCount(0);
        int gen1Started = GC.CollectionCount(1);
        int gen2Started = GC.CollectionCount(2);
        ExpectedValueSampleBatch batch = new DeckMonteCarloSimulator()
            .SimulateExpectedTotalSamples(
                deck.Cards,
                options,
                startRun,
                missing);
        series.TotalValuesByRun.AddRange(batch.TotalValuesByRun);

        double wallMs = Stopwatch.GetElapsedTime(wallStartedAt).TotalMilliseconds;
        double cpuMs = ReadProcessCpuMilliseconds() - cpuStartedMs;
        long allocatedBytes = Math.Max(
            0,
            GC.GetTotalAllocatedBytes(precise: false) - allocatedStarted);
        SearchBudgetTelemetrySnapshot budget = budgetTelemetry.Snapshot();
        MainFile.Logger.Info(
            $"[dEV] sample {DateTime.Now:HH:mm:ss.fff} kind={DescribeSimulationKind(simulationKey)} " +
            $"horizon={horizon} runs={startRun}+{missing} degree={runDegree} sliceMax={maximumRunsToAdd} " +
            $"branchGap={options.SelectiveThirdBranchMinScoreGap} nodeBudget={options.MaxSearchNodesPerTurn} " +
            $"wall={wallMs:0}ms cpu={cpuMs:0}ms " +
            $"avgCores={(wallMs > 0d ? cpuMs / wallMs : 0d):0.00} " +
            $"nodesAvg={budget.AverageNodes:0} nodesMax={budget.MaximumNodes} " +
            $"budgetTurns={budget.BudgetLimitedTurns}/{budget.TurnCount} " +
            $"alloc={allocatedBytes / (1024d * 1024d):0.0}MB " +
            $"gc={GC.CollectionCount(0) - gen0Started}/{GC.CollectionCount(1) - gen1Started}/{GC.CollectionCount(2) - gen2Started} " +
            $"pool={ThreadPool.ThreadCount}/{ThreadPool.PendingWorkItemCount}",
            0);
        return series;
    }

    private static int SampleCount(string simulationKey, int horizon)
    {
        return samplesBySimulation.TryGetValue(
            SampleSeriesKey(simulationKey, horizon),
            out SimulationSampleSeries? series)
            ? series.TotalValuesByRun.Count
            : 0;
    }

    private static double ReadProcessCpuMilliseconds()
    {
        try
        {
            using Process process = Process.GetCurrentProcess();
            return process.TotalProcessorTime.TotalMilliseconds;
        }
        catch
        {
            return double.NaN;
        }
    }

    private static string DescribeSimulationKind(string simulationKey)
    {
        if (simulationKey.Contains("|add:", StringComparison.Ordinal))
        {
            return "add";
        }

        if (simulationKey.Contains("|replace:", StringComparison.Ordinal))
        {
            return "replace";
        }

        if (simulationKey.Contains("|form:", StringComparison.Ordinal))
        {
            return "form";
        }

        if (simulationKey.Contains("|without:", StringComparison.Ordinal))
        {
            return "without";
        }

        return "baseline";
    }

    private static string SampleSeriesKey(string simulationKey, int horizon)
    {
        return $"{simulationKey}|horizon:{horizon}";
    }

    private static HorizonDeltaResult BuildPairedHorizonResult(
        SimulationSampleSeries before,
        SimulationSampleSeries after,
        int runs,
        int confidenceLevelPercent,
        int plannedStoppingLooks,
        int minimumRuns,
        int maximumRuns,
        bool earlyStoppingEnabled)
    {
        double[] differences = new double[runs];
        for (int run = 0; run < runs; run++)
        {
            differences[run] = after.TotalValuesByRun[run] - before.TotalValuesByRun[run];
        }

        PairedDeltaSummary summary = PairedDeltaStatistics.Calculate(
            differences,
            confidenceLevelPercent,
            plannedStoppingLooks);
        bool reachedMaximum = runs >= maximumRuns;
        bool stoppedEarly = earlyStoppingEnabled
            && runs >= minimumRuns
            && summary.HasStableSign;
        SamplingState state = reachedMaximum || stoppedEarly
            ? summary.HasStableSign ? SamplingState.Stable : SamplingState.MaxUncertain
            : runs == RealtimeSimulationSettings.RunBatchSize
                ? SamplingState.Preview
                : SamplingState.Refining;
        return new HorizonDeltaResult(
            summary.Mean,
            summary.LowerConfidence,
            summary.UpperConfidence,
            summary.LowerStopping,
            summary.UpperStopping,
            runs,
            state);
    }

    private static bool IsComplexRealtimeProbe(SimulationCard card)
    {
        return card.Draw > 0
            || card.DrawsToHandFull
            || card.IsPower
            || card.AutoPlay is not null
            || card.Actions.Any(action => action.Kind is
                "selectCards"
                or "moveCardBetweenPiles"
                or "transformCard"
                or "createCard"
                or "createCardChoices"
                or "autoPlay");
    }

    private static DeckSimulationOptions BuildOptions(
        int turns,
        LibraryForLayer lib,
        int runDegree,
        RealtimeSimulationSettings settings,
        IReadOnlyList<int> startingInstanceIds,
        SearchBudgetTelemetryCollector budgetTelemetry)
    {
        return new DeckSimulationOptions
        {
            Turns = turns,
            Runs = RealtimeSimulationSettings.RunBatchSize,
            RunDegreeOfParallelism = runDegree,
            Seed = SimulationSeed,
            HandSize = HandSize,
            MaxHandSize = MaxHandSize,
            BaseEnergy = BaseEnergy,
            BaseStars = BaseStars,
            StarsPersistBetweenTurns = true,
            MaxCardsPlayedPerTurn = ResolvedPlaySafetyCap,
            MaxBranchingCards = settings.Branch,
            SelectiveThirdBranchMinScoreGap = RealtimeSearchBranchPolicy.SelectiveThirdBranchMinScoreGap,
            MaxFullyBranchedCardsPlayedPerTurn = settings.TurnDepth,
            MaxSearchNodesPerTurn = RealtimeSearchBudgetPolicy.ResolveMaxSearchNodesPerTurn(turns),
            SearchBudgetTelemetry = budgetTelemetry,
            EnableLoopDetection = true,
            CardLibrary = lib.Library,
            GeneratedCardPools = generatedPools,
            StartingInstanceIds = startingInstanceIds,
            CounterfactualStableShuffle = true,
            WorkerThreadPriority = ThreadPriority.BelowNormal
        };
    }
    private static MappedDeck MapDeckWithStableIds(
        IReadOnlyList<DeckCardRef> cards,
        LibraryForLayer lib)
    {
        List<(string Token, int SourceIndex, SimulationCard Card)> mappedCards = [];
        int skipped = 0;
        for (int index = 0; index < cards.Count; index++)
        {
            DeckCardRef card = cards[index];
            SimulationCard? mapped = MapCard(card.Id, card.Upgrade, lib, EnchantmentOf(card));
            if (mapped is null)
            {
                skipped++;
                continue;
            }

            mappedCards.Add((
                CardToken(card.Id, card.Upgrade, EnchantmentOf(card)),
                index,
                mapped));
        }

        if (skipped > 0)
        {
            MainFile.Logger.Info($"RealtimeEvService: {skipped} deck cards not in simulation library (skipped).", 0);
        }

        // The live deck signature is order-independent, so its stable identities must be too.
        // Canonical token order keeps a persisted sample series attached to the same card identities
        // even if the game returns the same deck in a different list order after a reload. Identical
        // duplicates are interchangeable; SourceIndex only makes their local ordering deterministic.
        mappedCards.Sort((left, right) =>
        {
            int tokenComparison = string.Compare(left.Token, right.Token, StringComparison.Ordinal);
            return tokenComparison != 0
                ? tokenComparison
                : left.SourceIndex.CompareTo(right.SourceIndex);
        });

        return new MappedDeck(
            mappedCards.Select(item => item.Card).ToList(),
            Enumerable.Range(0, mappedCards.Count).ToList());
    }

    private static SimulationCard? MapCard(
        string id,
        int upgrade,
        LibraryForLayer lib,
        CardEnchantmentRef? enchantment = null)
    {
        string key = upgrade > 0 ? $"{id}+{upgrade}" : id;
        if (lib.ByModelId.TryGetValue(key, out SimulationCard? match))
        {
            return ApplyEnchantment(match, enchantment);
        }

        // Fall back to the unupgraded form if the exact upgrade level isn't modeled.
        return lib.ByModelId.TryGetValue(id, out SimulationCard? unupgraded)
            ? ApplyEnchantment(unupgraded, enchantment)
            : null;
    }

    private static SimulationCard ApplyEnchantment(SimulationCard card, CardEnchantmentRef? enchantment)
    {
        if (enchantment is not { } value || string.IsNullOrWhiteSpace(value.Id))
        {
            return card;
        }

        return card with
        {
            Enchantment = new SimulationEnchantment
            {
                Id = value.Id,
                Amount = Math.Max(1, value.Amount)
            }
        };
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

    private sealed class CacheFile
    {
        public int SchemaVersion { get; set; }

        public string ComputeKey { get; set; } = "";

        public Dictionary<string, SimulationSampleSeries> Samples { get; set; } = new();

        public Dictionary<string, PersistedCardEvResult> Results { get; set; } = new();
    }

    private sealed class PersistedCardEvResult
    {
        public HorizonDeltaResult? Short { get; set; }

        public HorizonDeltaResult? Mid { get; set; }

        public HorizonDeltaResult? Long { get; set; }

        public int MaxRuns { get; set; }
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
            if (data is null
                || data.SchemaVersion != 5
                || !string.Equals(data.ComputeKey, CacheComputeKey, StringComparison.Ordinal))
            {
                return;
            }

            foreach (KeyValuePair<string, SimulationSampleSeries> entry in data.Samples)
            {
                if (entry.Value.IsValid
                    && entry.Value.TotalValuesByRun.Count <= RealtimeSimulationSettings.MaximumAllowedRuns)
                {
                    samplesBySimulation.TryAdd(entry.Key, entry.Value);
                }
            }

            int loaded = 0;
            foreach (KeyValuePair<string, PersistedCardEvResult> entry in data.Results)
            {
                PersistedCardEvResult persisted = entry.Value;
                if (persisted.Short is null || persisted.Mid is null || persisted.Long is null)
                {
                    continue;
                }

                results.TryAdd(entry.Key, new CardEvResult
                {
                    Short = persisted.Short,
                    Mid = persisted.Mid,
                    Long = persisted.Long,
                    MaxRuns = persisted.MaxRuns,
                    Complete = true
                });
                loaded++;
            }

            MainFile.Logger.Info(
                $"RealtimeEvService: loaded {loaded} dEV results and {samplesBySimulation.Count} paired sample series.",
                0);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"RealtimeEvService: failed to load dEV cache: {ex.Message}", 0);
        }
    }

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
            CacheFile data = new()
            {
                SchemaVersion = 5,
                ComputeKey = CacheComputeKey
            };

            foreach (KeyValuePair<string, SimulationSampleSeries> entry in samplesBySimulation)
            {
                if (data.Samples.Count >= MaxPersistedEntries)
                {
                    break;
                }

                if (entry.Value.IsValid)
                {
                    data.Samples[entry.Key] = entry.Value;
                }
            }

            foreach (KeyValuePair<string, CardEvResult> entry in results)
            {
                if (data.Results.Count >= MaxPersistedEntries)
                {
                    break;
                }

                CardEvResult result = entry.Value;
                if (!result.Complete
                    || result.Failed
                    || result.Short is null
                    || result.Mid is null
                    || result.Long is null)
                {
                    continue;
                }

                data.Results[entry.Key] = new PersistedCardEvResult
                {
                    Short = result.Short,
                    Mid = result.Mid,
                    Long = result.Long,
                    MaxRuns = result.MaxRuns
                };
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
            MainFile.Logger.Warn($"RealtimeEvService: failed to save dEV cache: {ex.Message}", 0);
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

    public readonly record struct CardEnchantmentRef(string Id, int Amount);

    public readonly record struct DeckCardRef(
        string Id,
        int Upgrade,
        string? EnchantmentId = null,
        int? EnchantmentAmount = null);

    public sealed record DeckSnapshot(
        IReadOnlyList<DeckCardRef> Cards,
        int Layer,
        string Signature,
        RealtimeSimulationSettings Settings);

    public static CardEnchantmentRef? ReadCardEnchantment(MegaCrit.Sts2.Core.Models.CardModel card)
    {
        try
        {
            MegaCrit.Sts2.Core.Models.EnchantmentModel? enchantment = card.Enchantment;
            if (enchantment is null)
            {
                return null;
            }

            string id = enchantment.Id.ToString();
            return string.IsNullOrWhiteSpace(id)
                ? null
                : new CardEnchantmentRef(id, Math.Max(1, enchantment.Amount));
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"RealtimeEvService: failed to read card enchantment: {ex.Message}", 0);
            return null;
        }
    }

    private static CardEnchantmentRef? EnchantmentOf(DeckCardRef card)
    {
        return string.IsNullOrWhiteSpace(card.EnchantmentId)
            ? null
            : new CardEnchantmentRef(card.EnchantmentId, Math.Max(1, card.EnchantmentAmount ?? 1));
    }

    private static string CardToken(string? id, int upgrade, CardEnchantmentRef? enchantment)
    {
        string token = upgrade > 0 ? $"{id}+{upgrade}" : id ?? "";
        if (enchantment is not { } value || string.IsNullOrWhiteSpace(value.Id))
        {
            return token;
        }

        string enchantmentKey = SimulationEnchantment.NormalizeKey(value.Id);
        return $"{token}@{enchantmentKey}:{Math.Max(1, value.Amount)}";
    }

    private static bool MatchesEnchantment(SimulationEnchantment? cardEnchantment, CardEnchantmentRef? expected)
    {
        if (cardEnchantment is null || string.IsNullOrWhiteSpace(cardEnchantment.Id))
        {
            return expected is null || string.IsNullOrWhiteSpace(expected.Value.Id);
        }

        if (expected is not { } value || string.IsNullOrWhiteSpace(value.Id))
        {
            return false;
        }

        return string.Equals(
                SimulationEnchantment.NormalizeKey(cardEnchantment.Id),
                SimulationEnchantment.NormalizeKey(value.Id),
                StringComparison.OrdinalIgnoreCase)
            && Math.Max(1, cardEnchantment.Amount) == Math.Max(1, value.Amount);
    }

    internal static bool IsCurrentDeckCardInstance(MegaCrit.Sts2.Core.Models.CardModel card)
    {
        return LiveDeck.ContainsCardInstance(card);
    }

    internal static class LiveDeck
    {
        public static bool ContainsCardInstance(MegaCrit.Sts2.Core.Models.CardModel candidate)
        {
            try
            {
                MegaCrit.Sts2.Core.Runs.RunManager? manager = MegaCrit.Sts2.Core.Runs.RunManager.Instance;
                if (manager is null || !manager.IsInProgress)
                {
                    return false;
                }

                MegaCrit.Sts2.Core.Runs.RunState? state = manager.DebugOnlyGetState();
                if (state is null || state.Players.Count == 0)
                {
                    return false;
                }

                return state.Players[0].Deck.Cards.Any(card => ReferenceEquals(card, candidate));
            }
            catch (Exception ex)
            {
                MainFile.Logger.Warn($"RealtimeEvService: failed to identify inspect card ownership: {ex.Message}", 0);
                return false;
            }
        }

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

                    CardEnchantmentRef? enchantment = ReadCardEnchantment(card);
                    refs.Add(new DeckCardRef(
                        id,
                        card.CurrentUpgradeLevel,
                        enchantment?.Id,
                        enchantment?.Amount));
                }

                if (refs.Count == 0)
                {
                    return null;
                }

                int layer = ResolveLayer(state);
                RealtimeSimulationSettings settings = CardValueOverlayModConfig.CurrentSettings;
                string signature = $"{settings.CacheKey}|{BuildSignature(refs, layer)}";
                return new DeckSnapshot(refs, layer, signature, settings);
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
                .Select(r => CardToken(r.Id, r.Upgrade, EnchantmentOf(r)))
                .ToList();
            tokens.Sort(StringComparer.Ordinal);
            return $"L{layer}:" + string.Join(",", tokens);
        }
    }
}
