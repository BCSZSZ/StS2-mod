namespace CardValueOverlay.Modeling.Combat;

public sealed record CombatDrawOutcome(
    double Probability,
    IReadOnlyList<int> DrawnCards,
    IReadOnlyList<int> RemainingKnownTop,
    IReadOnlyList<int> RemainingUnknownCards,
    IReadOnlyList<int> RemainingDiscard,
    ulong StableKey,
    bool IsIdentity = false);

public sealed record CombatChanceOutcome(
    double Probability,
    CombatDrawOutcome Draw,
    IReadOnlyList<string> NextMonsterIntentStateIds,
    ulong StableKey);

public sealed class CombatChanceResolver
{
    private sealed record InstanceGroup(CombatCardInstanceKey Key, int[] InstanceIds);

    private readonly Dictionary<(CombatStateKey State, int DrawCount), IReadOnlyList<CombatDrawOutcome>> _drawCache = [];
    private readonly Dictionary<CombinedChanceCacheKey, IReadOnlyList<CombatChanceOutcome>> _combinedCache = [];

    private readonly record struct CombinedChanceCacheKey(
        IReadOnlyList<CombatDrawOutcome> Draws,
        ulong TransitionHash,
        CombatSolveMode Mode,
        int SparseLimit);

    private static readonly CombatDrawOutcome IdentityDraw = new(
        1d,
        [],
        [],
        [],
        [],
        0UL,
        true);

    private static readonly IReadOnlyList<CombatDrawOutcome> IdentityDrawList = [IdentityDraw];

    private static readonly IReadOnlyList<CombatChanceOutcome> IdentityChanceList =
    [new CombatChanceOutcome(1d, IdentityDraw, [], 0UL)];

    public IReadOnlyList<CombatDrawOutcome> EnumerateInitialDrawOutcomes(
        CombatInformationState state,
        CombatCardCatalog cards,
        int requestedCount,
        int maximumHandSize)
    {
        if (state.Hand.Count != 0 || state.KnownTop.Count != 0 ||
            state.Discard.Count != 0 || state.Exhaust.Count != 0 || state.Play.Count != 0)
        {
            throw new InvalidOperationException("Initial draw preparation requires untouched combat card piles.");
        }

        int[] innate = state.UnknownDraw
            .Where(instanceId => cards.Get(state.GetDefinitionId(instanceId)).Innate)
            .OrderBy(state.GetPhysicalKey)
            .ThenBy(instanceId => instanceId)
            .ToArray();
        if (innate.Length == 0)
        {
            return EnumerateDrawOutcomes(state, requestedCount, maximumHandSize);
        }

        int drawCount = Math.Min(maximumHandSize, Math.Max(requestedCount, innate.Length));
        CombatMutationJournal journal = new();
        int mark = journal.Mark();
        try
        {
            if (innate.Length <= maximumHandSize)
            {
                MoveInstances(state, innate, CombatPile.UnknownDraw, CombatPile.KnownTop, journal);
                return EnumerateDrawOutcomes(state, drawCount, maximumHandSize).ToArray();
            }

            int[] nonInnate = state.UnknownDraw
                .Where(instanceId => !cards.Get(state.GetDefinitionId(instanceId)).Innate)
                .OrderBy(state.GetPhysicalKey)
                .ThenBy(instanceId => instanceId)
                .ToArray();
            RemoveInstances(state, nonInnate, CombatPile.UnknownDraw, journal);

            CombatDrawOutcome[] innateOnly = EnumerateDrawOutcomes(state, maximumHandSize, maximumHandSize).ToArray();
            return innateOnly.Select(outcome =>
            {
                int[] remaining = outcome.RemainingUnknownCards.Concat(nonInnate)
                    .OrderBy(state.GetPhysicalKey)
                    .ThenBy(instanceId => instanceId)
                    .ToArray();
                ulong stableKey = ComputeStableKey(
                    state,
                    outcome.DrawnCards,
                    outcome.RemainingKnownTop,
                    remaining,
                    outcome.RemainingDiscard);
                return outcome with { RemainingUnknownCards = remaining, StableKey = stableKey };
            }).OrderBy(outcome => outcome.StableKey).ToArray();
        }
        finally
        {
            journal.UndoTo(state, mark);
        }
    }

    public IReadOnlyList<CombatDrawOutcome> EnumerateDrawOutcomes(
        CombatInformationState state,
        int requestedCount,
        int maximumHandSize)
    {
        int count = Math.Min(Math.Max(0, requestedCount), Math.Max(0, maximumHandSize - state.Hand.Count));
        if (count == 0)
        {
            return IdentityDrawList;
        }

        CombatStateKeyBuilder cacheKeyBuilder = new();
        cacheKeyBuilder.Add(count);
        cacheKeyBuilder.AddOrderedCardInstancesWithIdentity(state.KnownTop, state.CardInstances);
        cacheKeyBuilder.AddUnorderedCardInstancesWithIdentity(state.UnknownDraw, state.CardInstances);
        cacheKeyBuilder.AddUnorderedCardInstancesWithIdentity(state.Discard, state.CardInstances);
        (CombatStateKey State, int DrawCount) cacheKey = (cacheKeyBuilder.Build(), count);
        if (_drawCache.TryGetValue(cacheKey, out IReadOnlyList<CombatDrawOutcome>? cached))
        {
            return cached;
        }

        int knownDrawCount = Math.Min(count, state.KnownTop.Count);
        int[] knownDrawn = state.KnownTop.Take(knownDrawCount).ToArray();
        int[] remainingKnownTop = state.KnownTop.Skip(knownDrawCount).ToArray();
        int remaining = count - knownDrawCount;
        InstanceGroup[] unknown = GroupInstances(state, state.UnknownDraw);
        int unknownTotal = unknown.Sum(group => group.InstanceIds.Length);
        List<CombatDrawOutcome> outcomes = [];

        if (remaining <= unknownTotal)
        {
            EnumerateGroupedDraws(
                state,
                unknown,
                remaining,
                knownDrawn,
                remainingKnownTop,
                state.Discard.ToArray(),
                outcomes);
        }
        else
        {
            int[] forcedUnknown = unknown.SelectMany(group => group.InstanceIds).ToArray();
            int[] prefix = knownDrawn.Concat(forcedUnknown).ToArray();
            InstanceGroup[] discard = GroupInstances(state, state.Discard);
            int drawFromDiscard = Math.Min(
                remaining - unknownTotal,
                discard.Sum(group => group.InstanceIds.Length));
            EnumerateGroupedDraws(
                state,
                discard,
                drawFromDiscard,
                prefix,
                remainingKnownTop,
                [],
                outcomes);
        }

        outcomes.Sort(static (left, right) => left.StableKey.CompareTo(right.StableKey));
        _drawCache[cacheKey] = outcomes;
        return outcomes;
    }

    public IReadOnlyList<CombatChanceOutcome> Combine(
        IReadOnlyList<CombatDrawOutcome> drawOutcomes,
        IReadOnlyList<IReadOnlyList<MonsterIntentTransition>> monsterTransitions,
        CombatSolveMode solveMode,
        int sparseLimit)
    {
        if (drawOutcomes.Count == 1 && drawOutcomes[0].IsIdentity && monsterTransitions.All(distribution => distribution.Count == 0))
        {
            return IdentityChanceList;
        }

        ulong transitionHash = 1469598103934665603UL;
        foreach (IReadOnlyList<MonsterIntentTransition> distribution in monsterTransitions)
        {
            transitionHash = HashStable(transitionHash, distribution.Count);
            foreach (MonsterIntentTransition transition in distribution)
            {
                transitionHash = HashStable(transitionHash, transition.StateId);
                transitionHash = HashStable(transitionHash, BitConverter.DoubleToInt64Bits(transition.Probability));
            }
        }
        CombinedChanceCacheKey cacheKey = new(drawOutcomes, transitionHash, solveMode, sparseLimit);
        if (_combinedCache.TryGetValue(cacheKey, out IReadOnlyList<CombatChanceOutcome>? cached))
        {
            return cached;
        }

        List<CombatChanceOutcome> outcomes = [];
        string[] selected = new string[monsterTransitions.Count];
        foreach (CombatDrawOutcome draw in drawOutcomes)
        {
            RecurseTransitions(0, draw.Probability, draw, monsterTransitions, selected, outcomes);
        }

        outcomes.RemoveAll(static outcome => outcome.Probability <= 0);
        outcomes.Sort(static (left, right) => left.StableKey.CompareTo(right.StableKey));
        IReadOnlyList<CombatChanceOutcome> ordered = outcomes;
        if (solveMode == CombatSolveMode.Exact || ordered.Count <= sparseLimit)
        {
            _combinedCache[cacheKey] = ordered;
            return ordered;
        }

        CombatChanceOutcome[] selectedSparse = ordered
            .OrderByDescending(outcome => outcome.Probability)
            .ThenBy(outcome => outcome.StableKey)
            .Take(sparseLimit)
            .ToArray();
        double mass = selectedSparse.Sum(outcome => outcome.Probability);
        CombatChanceOutcome[] sparse = selectedSparse
            .Select(outcome => outcome with { Probability = outcome.Probability / mass })
            .OrderBy(outcome => outcome.StableKey)
            .ToArray();
        _combinedCache[cacheKey] = sparse;
        return sparse;
    }

    public void Apply(CombatInformationState state, CombatChanceOutcome outcome, CombatMutationJournal journal)
    {
        if (!outcome.Draw.IsIdentity)
        {
            ReplacePile(state, CombatPile.KnownTop, outcome.Draw.RemainingKnownTop, journal);
            ReplacePile(state, CombatPile.UnknownDraw, outcome.Draw.RemainingUnknownCards, journal);
            ReplacePile(state, CombatPile.Discard, outcome.Draw.RemainingDiscard, journal);
            foreach (int card in outcome.Draw.DrawnCards)
            {
                journal.AddPile(state, CombatPile.Hand, card);
            }
        }

        for (int index = 0; index < outcome.NextMonsterIntentStateIds.Count; index++)
        {
            if (outcome.NextMonsterIntentStateIds[index].Length > 0 && state.Monsters[index].IsAlive)
            {
                journal.SetIntent(state, index, outcome.NextMonsterIntentStateIds[index]);
            }
        }
    }

    private static void EnumerateGroupedDraws(
        CombatInformationState state,
        IReadOnlyList<InstanceGroup> source,
        int drawCount,
        IReadOnlyList<int> prefixDrawn,
        IReadOnlyList<int> remainingKnownTop,
        IReadOnlyList<int> remainingDiscard,
        List<CombatDrawOutcome> output)
    {
        int available = source.Sum(group => group.InstanceIds.Length);
        int actualDraw = Math.Min(drawCount, available);
        int[] selected = new int[source.Count];
        double denominator = Combination(available, actualDraw);
        EnumerateSelection(
            state,
            0,
            actualDraw,
            source,
            selected,
            prefixDrawn,
            remainingKnownTop,
            remainingDiscard,
            denominator,
            output);
    }

    private static void EnumerateSelection(
        CombatInformationState state,
        int index,
        int remaining,
        IReadOnlyList<InstanceGroup> source,
        int[] selected,
        IReadOnlyList<int> prefixDrawn,
        IReadOnlyList<int> remainingKnownTop,
        IReadOnlyList<int> remainingDiscard,
        double denominator,
        List<CombatDrawOutcome> output)
    {
        if (index == source.Count)
        {
            if (remaining != 0)
            {
                return;
            }

            List<int> drawn = new(prefixDrawn.Count + selected.Sum());
            drawn.AddRange(prefixDrawn);
            List<int> remainingUnknown = [];
            double numerator = 1d;
            for (int groupIndex = 0; groupIndex < source.Count; groupIndex++)
            {
                int[] instances = source[groupIndex].InstanceIds;
                int taken = selected[groupIndex];
                numerator *= Combination(instances.Length, taken);
                for (int instanceIndex = 0; instanceIndex < taken; instanceIndex++)
                {
                    drawn.Add(instances[instanceIndex]);
                }
                for (int instanceIndex = taken; instanceIndex < instances.Length; instanceIndex++)
                {
                    remainingUnknown.Add(instances[instanceIndex]);
                }
            }

            drawn.Sort((left, right) => CompareInstances(state, left, right));
            remainingUnknown.Sort((left, right) => CompareInstances(state, left, right));
            int[] sortedDiscard = remainingDiscard
                .OrderBy(state.GetPhysicalKey)
                .ThenBy(instanceId => instanceId)
                .ToArray();
            ulong key = ComputeStableKey(state, drawn, remainingKnownTop, remainingUnknown, sortedDiscard);
            output.Add(new CombatDrawOutcome(
                denominator <= 0 ? 1d : numerator / denominator,
                drawn,
                remainingKnownTop.ToArray(),
                remainingUnknown,
                sortedDiscard,
                key));
            return;
        }

        int availableAfter = 0;
        for (int next = index + 1; next < source.Count; next++)
        {
            availableAfter += source[next].InstanceIds.Length;
        }

        int minimum = Math.Max(0, remaining - availableAfter);
        int maximum = Math.Min(source[index].InstanceIds.Length, remaining);
        for (int take = minimum; take <= maximum; take++)
        {
            selected[index] = take;
            EnumerateSelection(
                state,
                index + 1,
                remaining - take,
                source,
                selected,
                prefixDrawn,
                remainingKnownTop,
                remainingDiscard,
                denominator,
                output);
        }
        selected[index] = 0;
    }

    private static void RecurseTransitions(
        int index,
        double probability,
        CombatDrawOutcome draw,
        IReadOnlyList<IReadOnlyList<MonsterIntentTransition>> distributions,
        string[] selected,
        List<CombatChanceOutcome> output)
    {
        if (index == distributions.Count)
        {
            ulong key = draw.StableKey;
            foreach (string stateId in selected)
            {
                key = HashStable(key, stateId);
            }
            output.Add(new CombatChanceOutcome(probability, draw, selected.ToArray(), key));
            return;
        }

        IReadOnlyList<MonsterIntentTransition> distribution = distributions[index];
        if (distribution.Count == 0)
        {
            selected[index] = string.Empty;
            RecurseTransitions(index + 1, probability, draw, distributions, selected, output);
            return;
        }

        foreach (MonsterIntentTransition transition in distribution)
        {
            selected[index] = transition.StateId;
            RecurseTransitions(index + 1, probability * transition.Probability, draw, distributions, selected, output);
        }
    }

    private static InstanceGroup[] GroupInstances(
        CombatInformationState state,
        IEnumerable<int> instanceIds) => instanceIds
        .GroupBy(state.GetPhysicalKey)
        .OrderBy(group => group.Key)
        .Select(group => new InstanceGroup(group.Key, group.Order().ToArray()))
        .ToArray();

    private static int CompareInstances(CombatInformationState state, int left, int right)
    {
        int physical = state.GetPhysicalKey(left).CompareTo(state.GetPhysicalKey(right));
        return physical != 0 ? physical : left.CompareTo(right);
    }

    private static ulong ComputeStableKey(
        CombatInformationState state,
        IReadOnlyList<int> drawn,
        IReadOnlyList<int> knownTop,
        IReadOnlyList<int> unknown,
        IReadOnlyList<int> discard)
    {
        ulong hash = 1469598103934665603UL;
        foreach (int value in drawn) hash = HashStable(hash, state.GetPhysicalKey(value));
        hash = HashStable(hash, -1);
        foreach (int value in knownTop) hash = HashStable(hash, state.GetPhysicalKey(value));
        hash = HashStable(hash, -2);
        foreach (int value in unknown.OrderBy(state.GetPhysicalKey).ThenBy(value => value))
        {
            hash = HashStable(hash, state.GetPhysicalKey(value));
        }
        hash = HashStable(hash, -3);
        foreach (int value in discard.OrderBy(state.GetPhysicalKey).ThenBy(value => value))
        {
            hash = HashStable(hash, state.GetPhysicalKey(value));
        }
        return hash;
    }

    private static ulong HashStable(ulong hash, CombatCardInstanceKey value)
    {
        hash = HashStable(hash, value.DefinitionId);
        return HashStable(hash, value.ForgeDamageBonus);
    }

    private static ulong HashStable(ulong hash, int value)
    {
        hash ^= unchecked((uint)value);
        return hash * 1099511628211UL;
    }

    private static ulong HashStable(ulong hash, long value)
    {
        hash = HashStable(hash, unchecked((int)value));
        return HashStable(hash, unchecked((int)(value >> 32)));
    }

    private static ulong HashStable(ulong hash, string value)
    {
        foreach (char character in value)
        {
            hash ^= character;
            hash *= 1099511628211UL;
        }
        return hash;
    }

    private static double Combination(int n, int k)
    {
        if (k < 0 || k > n)
        {
            return 0d;
        }
        k = Math.Min(k, n - k);
        double result = 1d;
        for (int index = 1; index <= k; index++)
        {
            result *= (n - k + index) / (double)index;
        }
        return result;
    }

    private static void MoveInstances(
        CombatInformationState state,
        IReadOnlyList<int> instanceIds,
        CombatPile source,
        CombatPile destination,
        CombatMutationJournal journal)
    {
        foreach (int instanceId in instanceIds)
        {
            int position = state.GetPile(source).IndexOf(instanceId);
            if (position < 0)
            {
                throw new InvalidOperationException($"Card instance {instanceId} was not found in {source}.");
            }
            journal.RemovePileAt(state, source, position);
            journal.AddPile(state, destination, instanceId);
        }
    }

    private static void RemoveInstances(
        CombatInformationState state,
        IReadOnlyList<int> instanceIds,
        CombatPile source,
        CombatMutationJournal journal)
    {
        foreach (int instanceId in instanceIds)
        {
            int position = state.GetPile(source).IndexOf(instanceId);
            if (position < 0)
            {
                throw new InvalidOperationException($"Card instance {instanceId} was not found in {source}.");
            }
            journal.RemovePileAt(state, source, position);
        }
    }

    private static void ReplacePile(
        CombatInformationState state,
        CombatPile pile,
        IReadOnlyList<int> replacement,
        CombatMutationJournal journal)
    {
        List<int> current = state.GetPile(pile);
        for (int index = current.Count - 1; index >= 0; index--)
        {
            journal.RemovePileAt(state, pile, index);
        }

        foreach (int card in replacement)
        {
            journal.AddPile(state, pile, card);
        }
    }
}
