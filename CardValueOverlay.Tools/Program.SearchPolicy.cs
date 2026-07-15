using System.Text.Json;
using CardValueOverlay.Core.Configuration;
using CardValueOverlay.Modeling.Estimation;
using CardValueOverlay.Modeling.Extraction;
using CardValueOverlay.Modeling.Simulation;

namespace CardValueOverlay.Tools;

internal static partial class Program
{
    private static int CollectSearchPolicyData(string[] args)
    {
        string outputRoot = GetOption(args, "--output") ?? "data";
        string trainingDecksPath = GetOption(args, "--training-decks")
            ?? Path.Combine("history-analysis", "data", "dashen_77_selected_16_decks.json");
        string outputJsonlPath = GetOption(args, "--output-jsonl")
            ?? Path.Combine(outputRoot, "generated", "search_policy", "search_policy_teacher.generated.jsonl");
        string factsPath = GetOption(args, "--facts")
            ?? Path.Combine(outputRoot, "extracted", "card_facts.generated.json");
        string membershipsPath = GetOption(args, "--memberships")
            ?? Path.Combine(outputRoot, "extracted", "card_pool_memberships.generated.json");
        string generatedCardPoolsPath = GetOption(args, "--generated-card-pools")
            ?? Path.Combine(outputRoot, "manual-tags", "simulation_generated_card_pools.json");
        string cardSetupValuesPath = GetOption(args, "--card-setup-values")
            ?? Path.Combine(outputRoot, "manual-tags", "card_setup_values.json");
        string calibrationPath = GetOption(args, "--calibration")
            ?? Path.Combine(outputRoot, "manual-tags", "model_calibration.json");

        int runs = GetIntOption(args, "--runs") ?? 50;
        int turns = GetIntOption(args, "--turns") ?? TrainingHorizonTurnCounts.Longline;
        int seed = GetIntOption(args, "--seed") ?? 1;
        int handSize = GetIntOption(args, "--hand-size") ?? 5;
        int maxHandSize = GetIntOption(args, "--max-hand-size") ?? 10;
        int baseEnergy = GetIntOption(args, "--energy") ?? 3;
        int baseStars = GetIntOption(args, "--stars") ?? 3;
        int maxCardsPlayed = GetIntOption(args, "--max-plays")
            ?? DeckSimulationOptions.DefaultResolvedPlaySafetyCap;
        int maxBranchingCards = GetIntOption(args, "--max-branch")
            ?? DeckSimulationOptions.DefaultBranchWidth;
        int teacherMaxBranchingCards = GetIntOption(args, "--teacher-max-branch") ?? 8;
        int teacherMaxCardsPlayed = GetIntOption(args, "--teacher-max-plays")
            ?? DeckSimulationOptions.DefaultResolvedPlaySafetyCap;
        int teacherForwardTurns = Math.Max(1, GetIntOption(args, "--teacher-forward-turns") ?? 4);
        int teacherRollouts = Math.Max(1, GetIntOption(args, "--teacher-rollouts") ?? 1);
        int maxDecisionGroups = GetIntOption(args, "--max-groups") ?? 200000;
        int? groupsPerDeckVariant = GetIntOption(args, "--groups-per-deck-variant");
        int candidateDeckCount = Math.Max(0, GetIntOption(args, "--candidate-decks") ?? 20);
        int? limitCards = GetIntOption(args, "--limit-cards");
        int? limitDecks = GetIntOption(args, "--limit-decks");
        int skipDecks = Math.Max(0, GetIntOption(args, "--skip-decks") ?? 0);
        string? candidateFilter = GetOption(args, "--candidate");

        if (!File.Exists(trainingDecksPath))
        {
            return Fail($"Missing training deck file at {trainingDecksPath}.");
        }

        if (!File.Exists(factsPath))
        {
            return Fail($"Missing card facts at {factsPath}. Run parse-card-facts first.");
        }

        if (!File.Exists(calibrationPath))
        {
            return Fail($"Missing calibration file at {calibrationPath}.");
        }

        JsonSerializerOptions jsonOptions = CreateTrainingJsonOptions();
        IReadOnlyList<CardFactCatalogEntry> entries =
            JsonSerializer.Deserialize<List<CardFactCatalogEntry>>(File.ReadAllText(factsPath), jsonOptions)
            ?? throw new InvalidOperationException($"Failed to read card facts from {factsPath}.");
        IReadOnlyList<CardPoolMembershipEntry> memberships = LoadOptionalCardPoolMemberships(membershipsPath, jsonOptions);
        GeneratedCardPoolCatalog generatedCardPools = LoadOptionalGeneratedCardPools(generatedCardPoolsPath, jsonOptions);
        CardSetupValueCatalog cardSetupValues = CardSetupValueCatalog.LoadOrEmpty(cardSetupValuesPath, jsonOptions);
        ValueCalibration calibration = ValueCalibration.Load(calibrationPath);
        TrainingDeckFile trainingDeckFile =
            JsonSerializer.Deserialize<TrainingDeckFile>(File.ReadAllText(trainingDecksPath), jsonOptions)
            ?? throw new InvalidOperationException($"Failed to read training decks from {trainingDecksPath}.");
        IReadOnlyList<TrainingDeck> trainingDecks = trainingDeckFile.Decks
            .Skip(skipDecks)
            .Take(limitDecks ?? int.MaxValue)
            .ToArray();
        if (trainingDecks.Count == 0)
        {
            return Fail("Training deck file did not contain any decks.");
        }

        int[] layers = trainingDecks
            .Select(TrainingLayer)
            .Distinct()
            .Order()
            .ToArray();
        Dictionary<int, IReadOnlyList<SimulationCard>> librariesByLayer = layers.ToDictionary(
            layer => layer,
            layer => new SimulationCardLibraryBuilder().Build(
                entries,
                calibration,
                layer,
                includeUpgrades: true,
                memberships,
                setupValues: cardSetupValues));
        Dictionary<int, Dictionary<string, SimulationCard>> byModelIdByLayer = librariesByLayer.ToDictionary(
            pair => pair.Key,
            pair => pair.Value
                .GroupBy(card => card.ModelId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase));
        Dictionary<int, Dictionary<string, SimulationCard>> byTypeNameByLayer = librariesByLayer.ToDictionary(
            pair => pair.Key,
            pair => pair.Value
                .GroupBy(card => card.TypeName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase));
        List<PreparedTrainingDeck> preparedDecks = trainingDecks
            .Select((deck, index) => PrepareTrainingDeck(deck, skipDecks + index, byModelIdByLayer, byTypeNameByLayer))
            .ToList();

        IReadOnlyList<TrainingCandidate> candidates = SelectTrainingCandidates(librariesByLayer[layers[0]]);
        if (!string.IsNullOrWhiteSpace(candidateFilter))
        {
            candidates = candidates
                .Where(candidate =>
                    string.Equals(candidate.ModelId, candidateFilter, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(candidate.TypeName, candidateFilter, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        candidates = candidates
            .Take(limitCards ?? int.MaxValue)
            .ToArray();

        string? parent = Path.GetDirectoryName(Path.GetFullPath(outputJsonlPath));
        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
        }

        using StreamWriter writer = new(outputJsonlPath);
        writer.AutoFlush = true;
        SearchPolicyDataCollector collector = new(writer, maxDecisionGroups);
        DeckMonteCarloSimulator simulator = new();

        IReadOnlyList<PreparedTrainingDeck> candidateDecks = SelectCandidateDecks(preparedDecks, candidateDeckCount);
        IReadOnlyList<SearchPolicyDeckVariant> deckVariants = BuildSearchPolicyDeckVariants(
            preparedDecks,
            candidateDecks,
            candidates,
            librariesByLayer);
        deckVariants = ShuffleDeckVariants(deckVariants, seed);

        int baselineSimulations = 0;
        int candidateSimulations = 0;
        for (int variantIndex = 0; variantIndex < deckVariants.Count; variantIndex++)
        {
            if (collector.Count >= maxDecisionGroups)
            {
                break;
            }

            SearchPolicyDeckVariant variant = deckVariants[variantIndex];
            int quota = groupsPerDeckVariant
                ?? DivideRoundUp(
                    maxDecisionGroups - collector.Count,
                    deckVariants.Count - variantIndex);
            collector.SetActiveLimit(collector.Count + Math.Max(0, quota));
            RunSearchPolicyCollectionSimulation(
                simulator,
                variant.Cards,
                variant.Deck,
                variant.Source,
                variant.Variant,
                turns,
                runs,
                seed,
                handSize,
                maxHandSize,
                baseEnergy,
                baseStars,
                maxCardsPlayed,
                maxBranchingCards,
                teacherMaxBranchingCards,
                teacherMaxCardsPlayed,
                teacherForwardTurns,
                teacherRollouts,
                librariesByLayer[variant.Deck.Layer],
                generatedCardPools,
                collector);

            if (string.Equals(variant.Source, "baseline", StringComparison.Ordinal))
            {
                baselineSimulations++;
            }
            else
            {
                candidateSimulations++;
            }

            int completed = variantIndex + 1;
            if (completed % 10 == 0 || completed == deckVariants.Count || collector.Count >= maxDecisionGroups)
            {
                Console.WriteLine($"collected deck variants {completed}/{deckVariants.Count}: baseline={baselineSimulations} candidate={candidateSimulations} groups={collector.Count}");
            }
        }

        Console.WriteLine("search policy teacher collection complete");
        Console.WriteLine($"trainingDecks: {preparedDecks.Count}");
        Console.WriteLine($"deckVariants: {deckVariants.Count}");
        Console.WriteLine($"baselineSimulations: {baselineSimulations}");
        Console.WriteLine($"candidateDecks: {candidateDecks.Count}");
        Console.WriteLine($"candidateFilters: {candidateFilter ?? "<none>"}");
        Console.WriteLine($"candidateVariants: {candidateSimulations}");
        Console.WriteLine($"runs: {runs}");
        Console.WriteLine($"turns: {turns}");
        Console.WriteLine($"maxGroups: {maxDecisionGroups}");
        Console.WriteLine($"groupsPerDeckVariant: {groupsPerDeckVariant?.ToString() ?? "<auto-equal>"}");
        Console.WriteLine($"groupsWritten: {collector.Count}");
        Console.WriteLine($"teacherMaxBranch: {teacherMaxBranchingCards}");
        Console.WriteLine($"teacherMaxPlays: {teacherMaxCardsPlayed}");
        Console.WriteLine($"teacherForwardTurns: {teacherForwardTurns}");
        Console.WriteLine($"teacherRollouts: {teacherRollouts}");
        Console.WriteLine($"output: {outputJsonlPath}");
        return 0;
    }

    private static void RunSearchPolicyCollectionSimulation(
        DeckMonteCarloSimulator simulator,
        IReadOnlyList<SimulationCard> deckCards,
        PreparedTrainingDeck deck,
        string source,
        string variant,
        int turns,
        int runs,
        int seed,
        int handSize,
        int maxHandSize,
        int baseEnergy,
        int baseStars,
        int maxCardsPlayed,
        int maxBranchingCards,
        int teacherMaxBranchingCards,
        int teacherMaxCardsPlayed,
        int teacherForwardTurns,
        int teacherRollouts,
        IReadOnlyList<SimulationCard> library,
        GeneratedCardPoolCatalog generatedCardPools,
        SearchPolicyDataCollector collector)
    {
        SearchPolicyGroupMetadata metadata = new(
            deck.RunId,
            deck.Index,
            variant,
            teacherMaxBranchingCards,
            teacherMaxCardsPlayed,
            teacherForwardTurns,
            teacherRollouts);
        DeckSimulationOptions options = BuildTrainingOptions(
            turns,
            runs,
            seed,
            deck.Index,
            handSize,
            maxHandSize,
            baseEnergy,
            baseStars,
            maxCardsPlayed,
            maxBranchingCards,
            library,
            generatedCardPools,
            runDegreeOfParallelism: 1,
            searchCardScorer: null,
            searchPolicyCollector: collector,
            searchPolicySource: source,
            searchPolicyMetadata: metadata);
        simulator.SimulateExpectedTurnValues(deckCards, options);
    }

    private static IReadOnlyList<SearchPolicyDeckVariant> BuildSearchPolicyDeckVariants(
        IReadOnlyList<PreparedTrainingDeck> preparedDecks,
        IReadOnlyList<PreparedTrainingDeck> candidateDecks,
        IReadOnlyList<TrainingCandidate> candidates,
        IReadOnlyDictionary<int, IReadOnlyList<SimulationCard>> librariesByLayer)
    {
        Dictionary<int, Dictionary<string, SimulationCard>> byModelIdByLayer = librariesByLayer.ToDictionary(
            pair => pair.Key,
            pair => pair.Value
                .GroupBy(card => card.ModelId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase));
        List<SearchPolicyDeckVariant> variants = preparedDecks
            .Select(deck => new SearchPolicyDeckVariant(
                deck,
                deck.Cards,
                "baseline",
                "baseline"))
            .ToList();
        foreach (TrainingCandidate candidate in candidates)
        {
            foreach ((SimulationCard Form, string Label) form in CandidateForms(candidate))
            {
                foreach (PreparedTrainingDeck deck in candidateDecks)
                {
                    SimulationCard layerCandidate = ResolveLayerCard(form.Form, byModelIdByLayer[deck.Layer]);
                    variants.Add(new SearchPolicyDeckVariant(
                        deck,
                        [.. deck.Cards, layerCandidate],
                        "candidate",
                        $"{candidate.ModelId}:{form.Label}"));
                }
            }
        }

        return variants;
    }

    private static IReadOnlyList<PreparedTrainingDeck> SelectCandidateDecks(
        IReadOnlyList<PreparedTrainingDeck> preparedDecks,
        int count)
    {
        if (count <= 0)
        {
            return [];
        }

        if (count >= preparedDecks.Count)
        {
            return preparedDecks;
        }

        IGrouping<string, PreparedTrainingDeck>[] groups = preparedDecks
            .GroupBy(deck => deck.Group, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToArray();
        List<PreparedTrainingDeck> selected = [];
        foreach (IGrouping<string, PreparedTrainingDeck> group in groups)
        {
            int groupQuota = Math.Min(
                group.Count(),
                (int)Math.Floor((double)count * group.Count() / preparedDecks.Count));
            selected.AddRange(group.Take(groupQuota));
        }

        foreach (IGrouping<string, PreparedTrainingDeck> group in groups
            .OrderByDescending(group => ((double)count * group.Count() / preparedDecks.Count) % 1d)
            .ThenBy(group => group.Key, StringComparer.Ordinal))
        {
            if (selected.Count >= count)
            {
                break;
            }

            foreach (PreparedTrainingDeck deck in group)
            {
                if (selected.Count >= count)
                {
                    break;
                }

                if (!selected.Contains(deck))
                {
                    selected.Add(deck);
                }
            }
        }

        return selected
            .OrderBy(deck => deck.Index)
            .ToArray();
    }

    private static IReadOnlyList<SearchPolicyDeckVariant> ShuffleDeckVariants(
        IReadOnlyList<SearchPolicyDeckVariant> variants,
        int seed)
    {
        SearchPolicyDeckVariant[] shuffled = variants.ToArray();
        Random rng = new(seed);
        for (int index = shuffled.Length - 1; index > 0; index--)
        {
            int swapIndex = rng.Next(index + 1);
            (shuffled[index], shuffled[swapIndex]) = (shuffled[swapIndex], shuffled[index]);
        }

        return shuffled;
    }

    private static int DivideRoundUp(int numerator, int denominator)
    {
        if (numerator <= 0 || denominator <= 0)
        {
            return 0;
        }

        return (numerator + denominator - 1) / denominator;
    }

    private static IReadOnlyList<(SimulationCard Form, string Label)> CandidateForms(TrainingCandidate candidate)
    {
        List<(SimulationCard Form, string Label)> forms = [(candidate.Unupgraded, "unupgraded")];
        if (candidate.Upgraded is not null)
        {
            forms.Add((candidate.Upgraded, "upgraded"));
        }

        return forms;
    }

    private sealed record SearchPolicyDeckVariant(
        PreparedTrainingDeck Deck,
        IReadOnlyList<SimulationCard> Cards,
        string Source,
        string Variant);
}
