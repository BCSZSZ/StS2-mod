namespace CardValueOverlay.Modeling.Combat.Portfolio;

public sealed class CombatPortfolioSampler
{
    public CombatSamplePlan BuildPlan(
        LoadedCombatPortfolio portfolio,
        IReadOnlyList<CombatDeckSnapshot> decks,
        IReadOnlyList<EncounterCombatDefinition> encounters,
        CombatCardCatalog cardCatalog,
        int seed)
    {
        CombatDeckSnapshotLoader deckLoader = new();
        Dictionary<string, CompiledCombatDeck> compiledDecks = decks.ToDictionary(
            deck => deck.SnapshotId,
            deck => deckLoader.CompileDeck(deck, cardCatalog),
            StringComparer.Ordinal);
        int totalProposal = portfolio.Definition.Cells.Sum(cell => cell.ProposalSamples);
        List<CombatSample> samples = [];
        List<string> warnings = [.. portfolio.Warnings];
        ulong rootKey = unchecked((ulong)(uint)seed);

        foreach (CombatPortfolioCell cell in portfolio.Definition.Cells.OrderBy(cell => cell.Id, StringComparer.Ordinal))
        {
            CombatDeckSnapshot[] cellDecks = decks
                .Where(deck => cell.DeckGroups.Contains(deck.Group, StringComparer.Ordinal))
                .OrderBy(deck => deck.RunId, StringComparer.Ordinal)
                .ToArray();
            EncounterCombatDefinition[] cellEncounters = encounters
                .Where(encounter => encounter.Act == cell.Act &&
                    string.Equals(encounter.Tier, cell.Tier, StringComparison.OrdinalIgnoreCase) &&
                    cell.EncounterSelectors.Any(selector => MatchesSelector(encounter, selector)))
                .OrderBy(encounter => encounter.ModelId, StringComparer.Ordinal)
                .ThenBy(encounter => encounter.RealizationId, StringComparer.Ordinal)
                .ToArray();
            IGrouping<string, EncounterCombatDefinition>[] encounterGroups = cellEncounters
                .GroupBy(encounter => encounter.ModelId, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .ToArray();
            if (cellDecks.Length == 0) warnings.Add($"{cell.Id}: no deck snapshots matched {string.Join(',', cell.DeckGroups)}.");
            if (encounterGroups.Length < cell.MinimumUniqueEncounters)
            {
                warnings.Add($"{cell.Id}: only {encounterGroups.Length} unique encounters matched; minimum is {cell.MinimumUniqueEncounters}.");
            }

            for (int ordinal = 0; ordinal < cell.ProposalSamples; ordinal++)
            {
                ulong sampleKey = SemanticRandomStreams.Derive(rootKey, "portfolio-sample", ordinal, cell.Id);
                CombatDeckSnapshot? deck = cellDecks.Length == 0 ? null : cellDecks[SemanticRandomStreams.Index(sampleKey, cellDecks.Length)];
                EncounterCombatDefinition? encounter = encounterGroups.Length == 0
                    ? null
                    : SelectRealization(
                        encounterGroups[ordinal % encounterGroups.Length].ToArray(),
                        SemanticRandomStreams.Derive(sampleKey, "encounter-realization", ordinal, cell.Id));
                int hp = cell.StartHp.Values[SemanticRandomStreams.Index(
                    SemanticRandomStreams.Derive(sampleKey, "start-hp", ordinal, cell.Id),
                    cell.StartHp.Values.Count)];
                CompiledCombatDeck? compiledDeck = deck is null ? null : compiledDecks[deck.SnapshotId];
                List<string> unsupported = [];
                if (compiledDeck is null) unsupported.Add("No stage-matched deck snapshot.");
                else if (!compiledDeck.IsSupported) unsupported.AddRange(compiledDeck.UnsupportedReasons);
                if (encounter is null) unsupported.Add("No encounter matched the cell.");
                else if (!encounter.IsSupported) unsupported.AddRange(encounter.UnsupportedReasons);

                double proposalProbability = 1d / totalProposal;
                double targetProbability = cell.TargetWeight / cell.ProposalSamples;
                samples.Add(new CombatSample(
                    $"{cell.Id}:{ordinal:D5}",
                    cell.Id,
                    cell.Act,
                    cell.Tier,
                    deck?.SnapshotId ?? "<missing>",
                    deck?.Group ?? "<missing>",
                    encounter?.StableId ?? "<missing>",
                    hp,
                    cell.StartHp.MaxHp,
                    cell.HpContextId,
                    sampleKey,
                    proposalProbability,
                    targetProbability,
                    targetProbability / proposalProbability,
                    unsupported.Count == 0,
                    unsupported.Count == 0 ? null : string.Join("; ", unsupported.Distinct(StringComparer.Ordinal))));
            }
        }

        return new CombatSamplePlan(
            portfolio.Definition.PortfolioId,
            portfolio.ContentHash,
            seed,
            samples,
            ComputeEffectiveSampleSize(samples.Where(sample => sample.Supported).Select(sample => sample.ImportanceWeight)),
            warnings.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
    }

    public static double ComputeEffectiveSampleSize(IEnumerable<double> weights)
    {
        double[] values = weights.Where(weight => weight > 0).ToArray();
        if (values.Length == 0) return 0d;
        double sum = values.Sum();
        double squareSum = values.Sum(weight => weight * weight);
        return sum * sum / squareSum;
    }

    public static bool MatchesSelector(EncounterCombatDefinition encounter, string selector)
    {
        int separator = selector.IndexOf(':');
        string kind = selector[..separator];
        string value = selector[(separator + 1)..];
        return kind.ToLowerInvariant() switch
        {
            "category" => string.Equals(encounter.Tier, value, StringComparison.OrdinalIgnoreCase),
            "modelid" => string.Equals(encounter.ModelId, value, StringComparison.OrdinalIgnoreCase),
            "typename" => string.Equals(encounter.TypeName, value, StringComparison.OrdinalIgnoreCase),
            "stableid" => string.Equals(encounter.StableId, value, StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private static EncounterCombatDefinition SelectRealization(
        IReadOnlyList<EncounterCombatDefinition> realizations,
        ulong key)
    {
        double draw = SemanticRandomStreams.UnitDouble(key);
        double cumulative = 0d;
        foreach (EncounterCombatDefinition realization in realizations.OrderBy(item => item.RealizationId, StringComparer.Ordinal))
        {
            cumulative += realization.RealizationProbability;
            if (draw < cumulative)
            {
                return realization;
            }
        }
        return realizations.OrderBy(item => item.RealizationId, StringComparer.Ordinal).Last();
    }
}
