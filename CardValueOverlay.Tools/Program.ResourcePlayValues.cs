using System.Globalization;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using CardValueOverlay.Modeling.Estimation;
using CardValueOverlay.Modeling.Extraction;
using CardValueOverlay.Modeling.Simulation;

namespace CardValueOverlay.Tools;

internal static partial class Program
{
    private static readonly ResourceProbeKind[] ResourceProbeKinds =
    [
        new("energyGain", "Energy +1"),
        new("draw", "Draw +1"),
        new("starGain", "Star +1")
    ];

    private static int EstimateResourcePlayValues(string[] args)
    {
        string outputRoot = GetOption(args, "--output") ?? "data";
        string trainingDecksPath = GetOption(args, "--training-decks")
            ?? Path.Combine("history-analysis", "data", "dashen_77_selected_16_decks.json");
        string factsPath = GetOption(args, "--facts")
            ?? Path.Combine(outputRoot, "extracted", "card_facts.generated.json");
        string membershipsPath = GetOption(args, "--memberships")
            ?? Path.Combine(outputRoot, "extracted", "card_pool_memberships.generated.json");
        string generatedCardPoolsPath = GetOption(args, "--generated-card-pools")
            ?? Path.Combine(outputRoot, "manual-tags", "simulation_generated_card_pools.json");
        string calibrationPath = GetOption(args, "--calibration")
            ?? Path.Combine(outputRoot, "manual-tags", "model_calibration.json");
        string setupPrioritiesPath = GetOption(args, "--setup-priorities")
            ?? Path.Combine(outputRoot, "manual-tags", "simulation_setup_priorities.json");
        int runs = GetIntOption(args, "--runs") ?? 100;
        int seed = GetIntOption(args, "--seed") ?? 1;
        int samplesPerDeck = GetIntOption(args, "--samples-per-deck") ?? 4;
        int handSize = GetIntOption(args, "--hand-size") ?? 5;
        int maxHandSize = GetIntOption(args, "--max-hand-size") ?? 10;
        int baseEnergy = GetIntOption(args, "--energy") ?? 3;
        int baseStars = GetIntOption(args, "--stars") ?? 3;
        int maxCardsPlayed = GetIntOption(args, "--max-plays") ?? 8;
        int maxBranchingCards = GetIntOption(args, "--max-branch") ?? 4;
        int? limitDecks = GetIntOption(args, "--limit-decks");
        int skipDecks = Math.Max(0, GetIntOption(args, "--skip-decks") ?? 0);
        bool profile = HasFlag(args, "--profile");
        string profileKind = GetOption(args, "--profile-kind") ?? "formal";
        string? benchmarkJsonPath = GetOption(args, "--benchmark-json");
        string? selectionNote = GetOption(args, "--selection-note");
        ISearchCardScorer? searchCardScorer = LoadSearchCardScorer(args);

        if (samplesPerDeck < 0)
        {
            return Fail("--samples-per-deck must be non-negative.");
        }

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

        string generatedRoot = Path.Combine(Path.GetFullPath(outputRoot), "generated");
        string resourceOutputRoot = Path.Combine(generatedRoot, "resource_play_values");
        Directory.CreateDirectory(resourceOutputRoot);
        string latestJsonPath = Path.Combine(resourceOutputRoot, "latest.generated.json");
        string latestReportPath = Path.Combine(resourceOutputRoot, "latest.generated.md");
        string outputJsonPath = GetOption(args, "--output-json") ?? latestJsonPath;
        string outputReportPath = GetOption(args, "--output-md") ?? Path.ChangeExtension(outputJsonPath, ".md");

        JsonSerializerOptions jsonOptions = CreateTrainingJsonOptions();
        IReadOnlyList<CardFactCatalogEntry> entries =
            JsonSerializer.Deserialize<List<CardFactCatalogEntry>>(File.ReadAllText(factsPath), jsonOptions)
            ?? throw new InvalidOperationException($"Failed to read card facts from {factsPath}.");
        IReadOnlyList<CardPoolMembershipEntry> memberships = LoadOptionalCardPoolMemberships(membershipsPath, jsonOptions);
        GeneratedCardPoolCatalog generatedCardPools = LoadOptionalGeneratedCardPools(generatedCardPoolsPath, jsonOptions);
        SimulationSetupPriorityCatalog setupPriorities = LoadOptionalSimulationSetupPriorities(setupPrioritiesPath, jsonOptions);
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

        ResourceProbeHorizonSpec[] horizons =
        [
            new("shortline", 4),
            new("midline", 8),
            new("longline", 14)
        ];
        int maxHorizonTurns = horizons.Max(horizon => horizon.Turns);
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
                setupPriorities));
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
        Dictionary<string, CardFactCatalogEntry> factsByBaseModelId = entries
            .GroupBy(entry => BaseModelId(entry.ModelId), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        List<PreparedTrainingDeck> preparedDecks = trainingDecks
            .Select((deck, index) => PrepareTrainingDeck(deck, skipDecks + index, byModelIdByLayer, byTypeNameByLayer))
            .ToList();

        List<ResourceProbeRecord> probes = [];
        List<ResourceDeckProfile> deckProfiles = [];
        List<string> warnings = [];
        Stopwatch totalStopwatch = Stopwatch.StartNew();
        for (int deckOrdinal = 0; deckOrdinal < preparedDecks.Count; deckOrdinal++)
        {
            Stopwatch deckStopwatch = Stopwatch.StartNew();
            PreparedTrainingDeck deck = preparedDecks[deckOrdinal];
            DeckSimulationOptions options = BuildTrainingOptions(
                maxHorizonTurns,
                runs,
                seed,
                deck.Index,
                handSize,
                maxHandSize,
                baseEnergy,
                baseStars,
                maxCardsPlayed,
                maxBranchingCards,
                librariesByLayer[deck.Layer],
                generatedCardPools,
                searchCardScorer: searchCardScorer);
            Stopwatch baselineStopwatch = Stopwatch.StartNew();
            DeckSimulationReport baselineReport = new DeckMonteCarloSimulator().Simulate(deck.Cards, options);
            baselineStopwatch.Stop();
            Dictionary<string, decimal> baselineValues = horizons.ToDictionary(
                horizon => horizon.Key,
                horizon => PrefixExpectedValue(baselineReport, horizon.Turns),
                StringComparer.OrdinalIgnoreCase);
            IReadOnlyList<ResourceProbeSelection> selections = SelectResourceProbeCopies(
                deck,
                factsByBaseModelId,
                samplesPerDeck,
                seed,
                warnings);

            Console.WriteLine($"resource baseline {deckOrdinal + 1}/{preparedDecks.Count}: deck={deck.Index} runId={deck.RunId} group={deck.Group} selectedCopies={selections.Count}");
            int probeSimulationCount = 0;
            foreach (ResourceProbeSelection selection in selections)
            {
                foreach (ResourceProbeKind kind in ResourceProbeKinds)
                {
                    string probeModelId = BuildProbeModelId(deck, selection, kind);
                    SimulationCard probeCard = BuildProbeCard(selection.OriginalCard, probeModelId, kind.Key);
                    SimulationCard[] variantDeck = deck.Cards.ToArray();
                    variantDeck[selection.CopyIndex] = probeCard;
                    DeckSimulationReport variantReport = new DeckMonteCarloSimulator().Simulate(variantDeck, options);
                    probeSimulationCount++;
                    Dictionary<string, ResourceProbeHorizonResult> horizonResults = [];
                    List<string> probeWarnings = [];
                    foreach (ResourceProbeHorizonSpec horizon in horizons)
                    {
                        decimal variantValue = PrefixExpectedValue(variantReport, horizon.Turns);
                        decimal delta = Round(variantValue - baselineValues[horizon.Key]);
                        decimal runScaledDelta = Round(delta * runs);
                        int playCount = PrefixPlayCount(variantReport, probeModelId, horizon.Turns);
                        decimal? valuePerPlay = playCount == 0
                            ? null
                            : Round(runScaledDelta / playCount);
                        bool valid = playCount > 0;
                        if (!valid)
                        {
                            probeWarnings.Add($"{kind.Key} {horizon.Key}: probe card was not played.");
                        }

                        horizonResults[horizon.Key] = new ResourceProbeHorizonResult(
                            baselineValues[horizon.Key],
                            variantValue,
                            delta,
                            runScaledDelta,
                            playCount,
                            valuePerPlay,
                            valid);
                    }

                    probes.Add(new ResourceProbeRecord(
                        deck.Index,
                        deck.RunId,
                        deck.Group,
                        deck.Layer,
                        selection.CopyIndex,
                        selection.OriginalCard.ModelId,
                        selection.OriginalCard.TypeName,
                        selection.OriginalCard.UpgradeLevel,
                        probeModelId,
                        kind.Key,
                        horizonResults,
                        probeWarnings));
                }
            }

            deckStopwatch.Stop();
            if (profile)
            {
                ResourceDeckProfile deckProfile = new(
                    deck.Index,
                    deck.RunId,
                    deck.Group,
                    deck.Layer,
                    deck.Cards.Count,
                    selections.Count,
                    probeSimulationCount,
                    RoundSeconds(baselineStopwatch.Elapsed.TotalSeconds),
                    RoundSeconds(deckStopwatch.Elapsed.TotalSeconds - baselineStopwatch.Elapsed.TotalSeconds),
                    RoundSeconds(deckStopwatch.Elapsed.TotalSeconds),
                    false,
                    []);
                deckProfiles.Add(deckProfile);
                Console.WriteLine($"profile deck={deck.Index} runId={deck.RunId} group={deck.Group} baselineSeconds={deckProfile.BaselineSeconds:0.###} probeSeconds={deckProfile.ProbeSeconds:0.###} totalSeconds={deckProfile.TotalSeconds:0.###}");
            }
        }
        totalStopwatch.Stop();

        ResourcePlayValueMetadata metadata = new(
            $"{Path.GetFileNameWithoutExtension(trainingDecksPath)}_resource_probe",
            DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            trainingDecksPath,
            preparedDecks.Count,
            skipDecks,
            limitDecks,
            runs,
            seed,
            samplesPerDeck,
            maxCardsPlayed,
            maxBranchingCards,
            horizons.ToDictionary(horizon => horizon.Key, horizon => horizon.Turns, StringComparer.OrdinalIgnoreCase),
            "Each selected deck copy is replaced by one DIY probe copy that preserves the original card and adds +1 energy, +1 draw, or +1 star. Simulator EV is expected value per run, so value/play is prefix EV delta divided by average prefix probe plays per run, equivalently delta * runs / total probe play count.");
        ResourceRunProfile? runProfile = profile
            ? BuildRunProfile(profileKind, totalStopwatch.Elapsed.TotalSeconds, deckProfiles, selectionNote)
            : null;
        ResourceRunProfile? benchmarkProfile = LoadBenchmarkProfile(benchmarkJsonPath, jsonOptions);
        IReadOnlyDictionary<string, ResourceAggregateOutput> resources = BuildResourceAggregates(probes, horizons);
        ResourcePlayValueOutput output = new(
            ResourcePlayValueOutput.CurrentSchemaVersion,
            metadata,
            runProfile,
            benchmarkProfile,
            resources,
            probes,
            warnings);

        string json = JsonSerializer.Serialize(output, jsonOptions);
        WriteTextWithRetry(outputJsonPath, json);
        string reportMarkdown = BuildResourcePlayValueReport(output);
        WriteTextWithRetry(outputReportPath, reportMarkdown);
        string archiveJsonPath = BuildResourcePlayValuesArchivePath(resourceOutputRoot, metadata, ".json");
        string archiveReportPath = BuildResourcePlayValuesArchivePath(resourceOutputRoot, metadata, ".md");
        CopyFileIfDifferent(outputJsonPath, latestJsonPath);
        CopyFileIfDifferent(outputJsonPath, archiveJsonPath);
        CopyFileIfDifferent(outputReportPath, latestReportPath);
        CopyFileIfDifferent(outputReportPath, archiveReportPath);

        Console.WriteLine("resource play-value probe complete");
        Console.WriteLine($"trainingDecks: {preparedDecks.Count}");
        Console.WriteLine($"runs: {runs}");
        Console.WriteLine($"samplesPerDeck: {samplesPerDeck}");
        Console.WriteLine($"maxBranchingCards: {maxBranchingCards}");
        Console.WriteLine($"probes: {probes.Count}");
        if (runProfile is not null)
        {
            Console.WriteLine($"profileKind: {runProfile.Kind}");
            Console.WriteLine($"elapsedSeconds: {runProfile.ElapsedSeconds:0.###}");
            Console.WriteLine($"slowDecks: {runProfile.SlowDeckCount}");
        }

        Console.WriteLine($"warnings: {warnings.Count + probes.Sum(probe => probe.Warnings.Count)}");
        Console.WriteLine($"output: {outputJsonPath}");
        Console.WriteLine($"latest: {latestJsonPath}");
        Console.WriteLine($"archive: {archiveJsonPath}");
        Console.WriteLine($"report: {latestReportPath}");
        return 0;
    }

    private static ResourceRunProfile? LoadBenchmarkProfile(string? benchmarkJsonPath, JsonSerializerOptions jsonOptions)
    {
        if (string.IsNullOrWhiteSpace(benchmarkJsonPath))
        {
            return null;
        }

        if (!File.Exists(benchmarkJsonPath))
        {
            throw new InvalidOperationException($"Missing benchmark JSON at {benchmarkJsonPath}.");
        }

        ResourcePlayValueOutput? benchmarkOutput =
            JsonSerializer.Deserialize<ResourcePlayValueOutput>(File.ReadAllText(benchmarkJsonPath), jsonOptions);
        if (benchmarkOutput is null)
        {
            throw new InvalidOperationException($"Failed to read benchmark JSON from {benchmarkJsonPath}.");
        }

        return benchmarkOutput.RunProfile;
    }

    private static ResourceRunProfile BuildRunProfile(
        string kind,
        double elapsedSeconds,
        IReadOnlyList<ResourceDeckProfile> deckProfiles,
        string? selectionNote)
    {
        double[] totals = deckProfiles
            .Select(profile => profile.TotalSeconds)
            .Order()
            .ToArray();
        double median = Percentile(totals, 0.5);
        double p75 = Percentile(totals, 0.75);
        double p25 = Percentile(totals, 0.25);
        double iqr = p75 - p25;
        double slowDeckThreshold = Math.Max(2.5 * median, p75 + (1.5 * iqr));
        double totalDeckSeconds = totals.Sum();
        double top3Share = totalDeckSeconds <= 0
            ? 0
            : deckProfiles
                .OrderByDescending(profile => profile.TotalSeconds)
                .Take(3)
                .Sum(profile => profile.TotalSeconds) / totalDeckSeconds;
        bool top3TooSlow = top3Share > 0.25;
        HashSet<int> top3Indexes = deckProfiles
            .OrderByDescending(profile => profile.TotalSeconds)
            .Take(3)
            .Select(profile => profile.DeckIndex)
            .ToHashSet();
        ResourceDeckProfile[] annotatedDecks = deckProfiles
            .Select(profile =>
            {
                List<string> reasons = [];
                if (profile.TotalSeconds > slowDeckThreshold)
                {
                    reasons.Add($"totalSeconds {profile.TotalSeconds:0.###} > slowDeckThreshold {slowDeckThreshold:0.###}");
                }

                if (top3TooSlow && top3Indexes.Contains(profile.DeckIndex))
                {
                    reasons.Add($"top3 cumulative share {top3Share:0.###} > 0.25");
                }

                return profile with
                {
                    Slow = reasons.Count > 0,
                    SlowReasons = reasons
                };
            })
            .ToArray();
        return new ResourceRunProfile(
            kind,
            RoundSeconds(elapsedSeconds),
            RoundSeconds(median),
            RoundSeconds(p75),
            RoundSeconds(p25),
            RoundSeconds(iqr),
            RoundSeconds(slowDeckThreshold),
            RoundSeconds(top3Share),
            annotatedDecks.Count(profile => profile.Slow),
            selectionNote,
            annotatedDecks);
    }

    private static double Percentile(IReadOnlyList<double> sortedValues, double percentile)
    {
        if (sortedValues.Count == 0)
        {
            return 0;
        }

        if (sortedValues.Count == 1)
        {
            return sortedValues[0];
        }

        double index = (sortedValues.Count - 1) * percentile;
        int lower = (int)Math.Floor(index);
        int upper = (int)Math.Ceiling(index);
        if (lower == upper)
        {
            return sortedValues[lower];
        }

        double ratio = index - lower;
        return sortedValues[lower] + ((sortedValues[upper] - sortedValues[lower]) * ratio);
    }

    private static double RoundSeconds(double value)
    {
        return Math.Round(value, 3, MidpointRounding.AwayFromZero);
    }

    private static IReadOnlyDictionary<string, ResourceAggregateOutput> BuildResourceAggregates(
        IReadOnlyList<ResourceProbeRecord> probes,
        IReadOnlyList<ResourceProbeHorizonSpec> horizons)
    {
        Dictionary<string, ResourceAggregateOutput> outputs = [];
        foreach (ResourceProbeKind kind in ResourceProbeKinds)
        {
            Dictionary<string, ResourceHorizonAggregate> horizonOutputs = [];
            foreach (ResourceProbeHorizonSpec horizon in horizons)
            {
                decimal totalDelta = 0m;
                decimal totalRunScaledDelta = 0m;
                int totalPlayCount = 0;
                decimal sampleSum = 0m;
                int validSamples = 0;
                int invalidSamples = 0;
                foreach (ResourceProbeRecord probe in probes.Where(item => item.Resource == kind.Key))
                {
                    ResourceProbeHorizonResult result = probe.Horizons[horizon.Key];
                    if (!result.Valid || result.ValuePerPlay is null)
                    {
                        invalidSamples++;
                        continue;
                    }

                    totalDelta += result.DeltaExpectedValue;
                    totalRunScaledDelta += result.RunScaledDeltaValue;
                    totalPlayCount += result.ProbePlayCount;
                    sampleSum += result.ValuePerPlay.Value;
                    validSamples++;
                }

                horizonOutputs[horizon.Key] = new ResourceHorizonAggregate(
                    Round(totalDelta),
                    Round(totalRunScaledDelta),
                    totalPlayCount,
                    totalPlayCount == 0 ? null : Round(totalRunScaledDelta / totalPlayCount),
                    validSamples == 0 ? null : Round(sampleSum / validSamples),
                    validSamples,
                    invalidSamples);
            }

            outputs[kind.Key] = new ResourceAggregateOutput(kind.Label, horizonOutputs);
        }

        return outputs;
    }

    private static IReadOnlyList<ResourceProbeSelection> SelectResourceProbeCopies(
        PreparedTrainingDeck deck,
        IReadOnlyDictionary<string, CardFactCatalogEntry> factsByBaseModelId,
        int samplesPerDeck,
        int seed,
        List<string> warnings)
    {
        List<int> eligibleCopyIndexes = [];
        for (int index = 0; index < deck.Cards.Count; index++)
        {
            SimulationCard card = deck.Cards[index];
            if (!card.IsPlayable || IsMultiplayerCard(card, factsByBaseModelId))
            {
                continue;
            }

            eligibleCopyIndexes.Add(index);
        }

        if (eligibleCopyIndexes.Count < samplesPerDeck)
        {
            warnings.Add($"deck={deck.Index} runId={deck.RunId}: only {eligibleCopyIndexes.Count} eligible playable non-multiplayer copies; requested {samplesPerDeck}.");
        }

        Random rng = new(unchecked(seed * 1_000_003 + deck.Index * 9_176 + 109));
        for (int index = eligibleCopyIndexes.Count - 1; index > 0; index--)
        {
            int swapIndex = rng.Next(index + 1);
            (eligibleCopyIndexes[index], eligibleCopyIndexes[swapIndex]) = (eligibleCopyIndexes[swapIndex], eligibleCopyIndexes[index]);
        }

        int take = Math.Min(samplesPerDeck, eligibleCopyIndexes.Count);
        ResourceProbeSelection[] selections = new ResourceProbeSelection[take];
        for (int index = 0; index < take; index++)
        {
            int copyIndex = eligibleCopyIndexes[index];
            selections[index] = new ResourceProbeSelection(index + 1, copyIndex, deck.Cards[copyIndex]);
        }

        return selections;
    }

    private static bool IsMultiplayerCard(
        SimulationCard card,
        IReadOnlyDictionary<string, CardFactCatalogEntry> factsByBaseModelId)
    {
        if (TargetContainsAlly(card.TargetType) || card.Actions.Any(action => TargetContainsAlly(action.TargetType)))
        {
            return true;
        }

        if (!factsByBaseModelId.TryGetValue(BaseModelId(card.ModelId), out CardFactCatalogEntry? fact))
        {
            return false;
        }

        return TargetContainsAlly(fact.TargetType)
            || fact.Actions.Any(action => TargetContainsAlly(action.TargetType));
    }

    private static bool TargetContainsAlly(string? targetType)
    {
        return targetType?.Contains("Ally", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static string BaseModelId(string modelId)
    {
        int separator = modelId.LastIndexOf('+');
        if (separator <= 0)
        {
            return modelId;
        }

        return int.TryParse(modelId[(separator + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out _)
            ? modelId[..separator]
            : modelId;
    }

    private static string BuildProbeModelId(
        PreparedTrainingDeck deck,
        ResourceProbeSelection selection,
        ResourceProbeKind kind)
    {
        return string.Join(
            ".",
            "PROBE",
            "RESOURCE",
            deck.Index.ToString(CultureInfo.InvariantCulture),
            selection.Ordinal.ToString(CultureInfo.InvariantCulture),
            kind.Key,
            SanitizeProbeModelComponent(selection.OriginalCard.ModelId));
    }

    private static string SanitizeProbeModelComponent(string value)
    {
        char[] chars = value
            .Select(ch => char.IsAsciiLetterOrDigit(ch) ? ch : '_')
            .ToArray();
        return new string(chars).Trim('_');
    }

    private static SimulationCard BuildProbeCard(
        SimulationCard original,
        string probeModelId,
        string resource)
    {
        return resource switch
        {
            "energyGain" => original with
            {
                ModelId = probeModelId,
                EnergyGain = original.EnergyGain + 1
            },
            "draw" => original with
            {
                ModelId = probeModelId,
                Draw = original.Draw + 1
            },
            "starGain" => original with
            {
                ModelId = probeModelId,
                StarGain = original.StarGain + 1
            },
            _ => throw new InvalidOperationException($"Unsupported resource probe {resource}.")
        };
    }

    private static decimal PrefixExpectedValue(DeckSimulationReport report, int turns)
    {
        if (report.Turns.Count < turns)
        {
            throw new InvalidOperationException($"Simulation result only has {report.Turns.Count} turns; cannot read {turns}-turn cumulative value.");
        }

        return Round(report.Turns.Take(turns).Sum(turn => turn.ExpectedValue));
    }

    private static int PrefixPlayCount(DeckSimulationReport report, string modelId, int turns)
    {
        return report.PlayedCardsByTurn
            .Where(card => card.Turn <= turns && string.Equals(card.ModelId, modelId, StringComparison.OrdinalIgnoreCase))
            .Sum(card => card.PlayCount);
    }

    private static string BuildResourcePlayValuesArchivePath(
        string resourceOutputRoot,
        ResourcePlayValueMetadata metadata,
        string extension)
    {
        string timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        string source = SanitizeFileComponent(metadata.Source);
        string fileName = string.Join(
            "_",
            timestamp,
            source,
            $"d{metadata.DeckCount}",
            $"r{metadata.Runs}",
            $"b{metadata.MaxBranchingCards}",
            $"s{metadata.SamplesPerDeck}") + ".generated" + extension;
        return Path.Combine(resourceOutputRoot, fileName);
    }

    private static string BuildResourcePlayValueReport(ResourcePlayValueOutput output)
    {
        StringBuilder builder = new();
        builder.AppendLine("# Resource Play Values");
        builder.AppendLine();
        builder.AppendLine($"Generated: {output.Metadata.GeneratedAt}");
        builder.AppendLine($"Decks: {output.Metadata.DeckCount}");
        builder.AppendLine($"Runs: {output.Metadata.Runs}");
        builder.AppendLine($"Samples/deck: {output.Metadata.SamplesPerDeck}");
        builder.AppendLine($"Max branch: {output.Metadata.MaxBranchingCards}");
        builder.AppendLine();
        if (output.BenchmarkProfile is not null)
        {
            AppendResourceRunProfile(builder, "Benchmark Summary", output.BenchmarkProfile);
        }

        if (output.RunProfile is not null)
        {
            string title = string.Equals(output.RunProfile.Kind, "benchmark", StringComparison.OrdinalIgnoreCase)
                ? "Benchmark Summary"
                : "Run Timing Summary";
            AppendResourceRunProfile(builder, title, output.RunProfile);
        }

        builder.AppendLine("## Aggregates");
        builder.AppendLine();
        builder.AppendLine("| Resource | Horizon | Weighted value/play | Sample mean value/play | Expected delta sum | Run-scaled delta sum | Probe plays | Valid | Invalid |");
        builder.AppendLine("| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");
        foreach ((string resource, ResourceAggregateOutput aggregate) in output.Resources.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            foreach ((string horizon, ResourceHorizonAggregate value) in aggregate.Horizons.OrderBy(pair => HorizonSortKey(pair.Key)))
            {
                builder.AppendLine(
                    $"| {EscapeMarkdown(resource)} | {EscapeMarkdown(horizon)} | {FormatNullable(value.WeightedValuePerPlay)} | "
                    + $"{FormatNullable(value.SampleMeanValuePerPlay)} | {value.TotalDeltaExpectedValue:0.###} | "
                    + $"{value.TotalRunScaledDeltaValue:0.###} | {value.TotalProbePlayCount} | "
                    + $"{value.ValidSampleCount} | {value.InvalidSampleCount} |");
            }
        }

        if (output.Warnings.Count > 0 || output.Probes.Any(probe => probe.Warnings.Count > 0))
        {
            builder.AppendLine();
            builder.AppendLine("## Warnings");
            builder.AppendLine();
            foreach (string warning in output.Warnings)
            {
                builder.AppendLine($"- {EscapeMarkdown(warning)}");
            }

            foreach (ResourceProbeRecord probe in output.Probes.Where(probe => probe.Warnings.Count > 0))
            {
                foreach (string warning in probe.Warnings)
                {
                    builder.AppendLine($"- deck={probe.DeckIndex} {EscapeMarkdown(probe.OriginalTypeName)} {probe.Resource}: {EscapeMarkdown(warning)}");
                }
            }
        }

        builder.AppendLine();
        builder.AppendLine("## Probe Details");
        builder.AppendLine();
        builder.AppendLine("| Deck | Group | Layer | Copy | Card | Resource | Short plays | Short value/play | Mid plays | Mid value/play | Long plays | Long value/play |");
        builder.AppendLine("| ---: | --- | ---: | ---: | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |");
        foreach (ResourceProbeRecord probe in output.Probes
            .OrderBy(probe => probe.DeckIndex)
            .ThenBy(probe => probe.CopyIndex)
            .ThenBy(probe => probe.Resource, StringComparer.Ordinal))
        {
            ResourceProbeHorizonResult shortline = probe.Horizons["shortline"];
            ResourceProbeHorizonResult midline = probe.Horizons["midline"];
            ResourceProbeHorizonResult longline = probe.Horizons["longline"];
            builder.AppendLine(
                $"| {probe.DeckIndex} | {EscapeMarkdown(probe.Group)} | {probe.Layer} | {probe.CopyIndex} | "
                + $"{EscapeMarkdown(probe.OriginalTypeName)} | {EscapeMarkdown(probe.Resource)} | "
                + $"{shortline.ProbePlayCount} | {FormatNullable(shortline.ValuePerPlay)} | "
                + $"{midline.ProbePlayCount} | {FormatNullable(midline.ValuePerPlay)} | "
                + $"{longline.ProbePlayCount} | {FormatNullable(longline.ValuePerPlay)} |");
        }

        return builder.ToString();
    }

    private static void AppendResourceRunProfile(
        StringBuilder builder,
        string title,
        ResourceRunProfile profile)
    {
        builder.AppendLine($"## {title}");
        builder.AppendLine();
        builder.AppendLine($"Kind: {profile.Kind}");
        builder.AppendLine($"Elapsed seconds: {profile.ElapsedSeconds:0.###}");
        builder.AppendLine($"Median deck seconds: {profile.MedianDeckSeconds:0.###}");
        builder.AppendLine($"P75 deck seconds: {profile.P75DeckSeconds:0.###}");
        builder.AppendLine($"Slow deck threshold: {profile.SlowDeckThresholdSeconds:0.###}");
        builder.AppendLine($"Top3 share: {profile.Top3Share:0.###}");
        builder.AppendLine($"Slow decks: {profile.SlowDeckCount}");
        if (!string.IsNullOrWhiteSpace(profile.SelectionNote))
        {
            builder.AppendLine($"Selection note: {EscapeMarkdown(profile.SelectionNote)}");
        }

        builder.AppendLine();
        builder.AppendLine("| Deck | RunId | Group | Cards | Samples | Probes | Baseline s | Probe s | Total s | Slow |");
        builder.AppendLine("| ---: | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |");
        foreach (ResourceDeckProfile deck in profile.Decks.OrderBy(deck => deck.DeckIndex))
        {
            string slow = deck.Slow ? EscapeMarkdown(string.Join("; ", deck.SlowReasons)) : "";
            builder.AppendLine(
                $"| {deck.DeckIndex} | {EscapeMarkdown(deck.RunId)} | {EscapeMarkdown(deck.Group)} | {deck.CardCount} | "
                + $"{deck.SampledCopyCount} | {deck.ProbeSimulationCount} | {deck.BaselineSeconds:0.###} | "
                + $"{deck.ProbeSeconds:0.###} | {deck.TotalSeconds:0.###} | {slow} |");
        }

        builder.AppendLine();
    }

    private static int HorizonSortKey(string horizon)
    {
        return horizon switch
        {
            "shortline" => 0,
            "midline" => 1,
            "longline" => 2,
            _ => 99
        };
    }

    private static string EscapeMarkdown(string value)
    {
        return value.Replace("|", "\\|", StringComparison.Ordinal);
    }

    private sealed record ResourceProbeKind(
        string Key,
        string Label);

    private sealed record ResourceProbeHorizonSpec(
        string Key,
        int Turns);

    private sealed record ResourceProbeSelection(
        int Ordinal,
        int CopyIndex,
        SimulationCard OriginalCard);

    private sealed record ResourcePlayValueOutput(
        int SchemaVersion,
        ResourcePlayValueMetadata Metadata,
        ResourceRunProfile? RunProfile,
        ResourceRunProfile? BenchmarkProfile,
        IReadOnlyDictionary<string, ResourceAggregateOutput> Resources,
        IReadOnlyList<ResourceProbeRecord> Probes,
        IReadOnlyList<string> Warnings)
    {
        public const int CurrentSchemaVersion = 1;
    }

    private sealed record ResourcePlayValueMetadata(
        string Source,
        string GeneratedAt,
        string TrainingDecksPath,
        int DeckCount,
        int TrainingDeckOffset,
        int? TrainingDeckLimit,
        int Runs,
        int Seed,
        int SamplesPerDeck,
        int MaxCardsPlayedPerTurn,
        int MaxBranchingCards,
        IReadOnlyDictionary<string, int> Horizons,
        string Note);

    private sealed record ResourceAggregateOutput(
        string Label,
        IReadOnlyDictionary<string, ResourceHorizonAggregate> Horizons);

    private sealed record ResourceRunProfile(
        string Kind,
        double ElapsedSeconds,
        double MedianDeckSeconds,
        double P75DeckSeconds,
        double P25DeckSeconds,
        double IqrDeckSeconds,
        double SlowDeckThresholdSeconds,
        double Top3Share,
        int SlowDeckCount,
        string? SelectionNote,
        IReadOnlyList<ResourceDeckProfile> Decks);

    private sealed record ResourceDeckProfile(
        int DeckIndex,
        string RunId,
        string Group,
        int Layer,
        int CardCount,
        int SampledCopyCount,
        int ProbeSimulationCount,
        double BaselineSeconds,
        double ProbeSeconds,
        double TotalSeconds,
        bool Slow,
        IReadOnlyList<string> SlowReasons);

    private sealed record ResourceHorizonAggregate(
        decimal TotalDeltaExpectedValue,
        decimal TotalRunScaledDeltaValue,
        int TotalProbePlayCount,
        decimal? WeightedValuePerPlay,
        decimal? SampleMeanValuePerPlay,
        int ValidSampleCount,
        int InvalidSampleCount);

    private sealed record ResourceProbeRecord(
        int DeckIndex,
        string RunId,
        string Group,
        int Layer,
        int CopyIndex,
        string OriginalModelId,
        string OriginalTypeName,
        int OriginalUpgradeLevel,
        string ProbeModelId,
        string Resource,
        IReadOnlyDictionary<string, ResourceProbeHorizonResult> Horizons,
        IReadOnlyList<string> Warnings);

    private sealed record ResourceProbeHorizonResult(
        decimal BaselineExpectedValue,
        decimal VariantExpectedValue,
        decimal DeltaExpectedValue,
        decimal RunScaledDeltaValue,
        int ProbePlayCount,
        decimal? ValuePerPlay,
        bool Valid);
}
