using System.Text.Json;
using CardValueOverlay.Modeling.Estimation;
using CardValueOverlay.Modeling.Extraction;
using CardValueOverlay.Modeling.Simulation;

namespace CardValueOverlay.Modeling.Export;

public sealed class GeneratedDataWriter
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public void WriteAll(ExtractionRunResult result, ModelingExtractionOptions options)
    {
        ExtractionPaths paths = ExtractionPaths.FromOptions(options);
        Directory.CreateDirectory(paths.ExtractedOutputRoot);
        Directory.CreateDirectory(paths.GeneratedOutputRoot);
        Directory.CreateDirectory(paths.ManualTagsRoot);

        WriteJson(Path.Combine(paths.ExtractedOutputRoot, "game_version.generated.json"), result.GameVersion);
        WriteJson(Path.Combine(paths.ExtractedOutputRoot, "card_catalog.generated.json"), result.Cards);
        WriteJson(Path.Combine(paths.ExtractedOutputRoot, "enemy_catalog.generated.json"), result.Enemies);
        WriteJson(Path.Combine(paths.ExtractedOutputRoot, "encounter_catalog.generated.json"), result.Encounters);
        WriteJson(Path.Combine(paths.ExtractedOutputRoot, "intent_catalog.generated.json"), result.Intents);
        WriteJson(Path.Combine(paths.ExtractedOutputRoot, "localization.generated.json"), result.Localization);
        WriteJson(Path.Combine(paths.GeneratedOutputRoot, "unresolved_extraction_items.generated.json"), result.UnresolvedItems);
        WriteMarkdown(Path.Combine(paths.GeneratedOutputRoot, "unresolved_extraction_items.md"), result.UnresolvedItems);
    }

    public void WriteCardEffectTerms(
        IReadOnlyList<CardEffectTermCatalogEntry> entries,
        ModelingExtractionOptions options)
    {
        ExtractionPaths paths = ExtractionPaths.FromOptions(options);
        Directory.CreateDirectory(paths.ExtractedOutputRoot);
        WriteJson(Path.Combine(paths.ExtractedOutputRoot, "card_effect_terms.generated.json"), entries);
    }

    public void WriteMonsterMoveProfiles(
        IReadOnlyList<MonsterMoveProfileEntry> entries,
        ModelingExtractionOptions options)
    {
        ExtractionPaths paths = ExtractionPaths.FromOptions(options);
        Directory.CreateDirectory(paths.ExtractedOutputRoot);
        WriteJson(Path.Combine(paths.ExtractedOutputRoot, "monster_move_profiles.generated.json"), entries);
    }

    public void WriteEncounterPatterns(
        IReadOnlyList<EncounterPatternEntry> entries,
        ModelingExtractionOptions options)
    {
        ExtractionPaths paths = ExtractionPaths.FromOptions(options);
        Directory.CreateDirectory(paths.ExtractedOutputRoot);
        Directory.CreateDirectory(paths.GeneratedOutputRoot);
        WriteJson(Path.Combine(paths.ExtractedOutputRoot, "encounter_patterns.generated.json"), entries);
        WriteEncounterPatternMarkdown(Path.Combine(paths.GeneratedOutputRoot, "encounter_patterns.md"), entries);
    }

    public void WriteCardPoolMemberships(
        IReadOnlyList<CardPoolMembershipEntry> entries,
        ModelingExtractionOptions options)
    {
        ExtractionPaths paths = ExtractionPaths.FromOptions(options);
        Directory.CreateDirectory(paths.ExtractedOutputRoot);
        WriteJson(Path.Combine(paths.ExtractedOutputRoot, "card_pool_memberships.generated.json"), entries);
    }

    public void WriteCardValueCandidates(IReadOnlyList<CardValueEstimate> estimates, string outputRoot)
    {
        string generatedRoot = Path.Combine(Path.GetFullPath(outputRoot), "generated");
        Directory.CreateDirectory(generatedRoot);
        WriteJson(Path.Combine(generatedRoot, "card_value_candidates.generated.json"), estimates);
        WriteCandidateMarkdown(Path.Combine(generatedRoot, "card_value_candidates.md"), estimates);
    }

    public void WriteEnemyExpectations(IReadOnlyList<EnemyExpectationProfile> profiles, string outputRoot)
    {
        string generatedRoot = Path.Combine(Path.GetFullPath(outputRoot), "generated");
        Directory.CreateDirectory(generatedRoot);
        WriteJson(Path.Combine(generatedRoot, "enemy_expectations.generated.json"), profiles);
        WriteEnemyExpectationMarkdown(Path.Combine(generatedRoot, "enemy_expectations.md"), profiles);
    }

    public void WriteDefenseCalibrationReport(DefenseCalibrationReport report, string outputRoot)
    {
        string generatedRoot = Path.Combine(Path.GetFullPath(outputRoot), "generated");
        Directory.CreateDirectory(generatedRoot);
        WriteJson(Path.Combine(generatedRoot, "defense_calibration.generated.json"), report);
        WriteDefenseCalibrationMarkdown(Path.Combine(generatedRoot, "defense_calibration.md"), report);
    }

    public void WriteEncounterWeightedEnemyPressureReport(
        EncounterWeightedEnemyPressureReport report,
        string outputRoot)
    {
        string generatedRoot = Path.Combine(Path.GetFullPath(outputRoot), "generated");
        Directory.CreateDirectory(generatedRoot);
        WriteJson(Path.Combine(generatedRoot, "encounter_weighted_enemy_pressure.generated.json"), report);
        WriteEncounterWeightedEnemyPressureMarkdown(
            Path.Combine(generatedRoot, "encounter_weighted_enemy_pressure.md"),
            report);
    }

    public void WriteCardValueReviewList(
        IReadOnlyList<CardValueEstimate> estimates,
        IReadOnlyList<CardPoolMembershipEntry> memberships,
        string outputRoot)
    {
        string generatedRoot = Path.Combine(Path.GetFullPath(outputRoot), "generated");
        Directory.CreateDirectory(generatedRoot);
        new CardValueReviewReportWriter().Write(
            Path.Combine(generatedRoot, "card_value_review_list.md"),
            estimates,
            memberships);
    }

    public void WriteSimulationCardLibrary(IReadOnlyList<SimulationCard> cards, string outputRoot)
    {
        string generatedRoot = Path.Combine(Path.GetFullPath(outputRoot), "generated");
        Directory.CreateDirectory(generatedRoot);
        WriteJson(Path.Combine(generatedRoot, "simulation_card_library.generated.json"), cards);
        WriteSimulationCardLibraryMarkdown(Path.Combine(generatedRoot, "simulation_card_library.md"), cards);
    }

    public void WriteDeckSimulationReport(DeckSimulationReport report, string outputRoot)
    {
        string generatedRoot = Path.Combine(Path.GetFullPath(outputRoot), "generated");
        Directory.CreateDirectory(generatedRoot);
        WriteJson(Path.Combine(generatedRoot, "deck_simulation.generated.json"), report);
        WriteDeckSimulationMarkdown(Path.Combine(generatedRoot, "deck_simulation.md"), report);
    }

    private void WriteJson<T>(string path, T value)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(value, _jsonOptions));
    }

    private static void WriteMarkdown(string path, IReadOnlyList<UnresolvedExtractionItem> items)
    {
        using StreamWriter writer = new(path);
        writer.WriteLine("# Unresolved Extraction Items");
        writer.WriteLine();
        writer.WriteLine("| Area | Severity | Message | Recommended action |");
        writer.WriteLine("| --- | --- | --- | --- |");
        foreach (UnresolvedExtractionItem item in items)
        {
            writer.WriteLine($"| {Escape(item.Area)} | {Escape(item.Severity)} | {Escape(item.Message)} | {Escape(item.RecommendedAction)} |");
        }
    }

    private static string Escape(string value)
    {
        return value.Replace("|", "\\|", StringComparison.Ordinal);
    }

    private static void WriteCandidateMarkdown(string path, IReadOnlyList<CardValueEstimate> estimates)
    {
        using StreamWriter writer = new(path);
        writer.WriteLine("# Card Value Candidates");
        writer.WriteLine();
        writer.WriteLine("| Card | Cost | Type | Value | Upgraded | Smith | Confidence | Warnings |");
        writer.WriteLine("| --- | ---: | --- | ---: | ---: | ---: | ---: | ---: |");
        foreach (CardValueEstimate estimate in estimates.OrderByDescending(item => item.EstimatedValue).ThenBy(item => item.TypeName, StringComparer.Ordinal))
        {
            writer.WriteLine(
                $"| {Escape(estimate.TypeName)} | {estimate.Cost?.ToString() ?? ""} | {Escape(estimate.CardType ?? "")} | {estimate.EstimatedValue:0.###} | {estimate.UpgradedEstimatedValue:0.###} | {estimate.SmithValue:0.###} | {estimate.Confidence:0.###} | {estimate.Warnings.Count} |");
        }
    }

    private static void WriteSimulationCardLibraryMarkdown(string path, IReadOnlyList<SimulationCard> cards)
    {
        using StreamWriter writer = new(path);
        writer.WriteLine("# Simulation Card Library");
        writer.WriteLine();
        writer.WriteLine("| Metric | Value |");
        writer.WriteLine("| --- | ---: |");
        writer.WriteLine($"| Cards | {cards.Count} |");
        writer.WriteLine($"| Playable | {cards.Count(card => card.IsPlayable)} |");
        writer.WriteLine($"| Star cost | {cards.Count(card => card.StarCost > 0)} |");
        writer.WriteLine($"| Star gain | {cards.Count(card => card.StarGain > 0)} |");
        writer.WriteLine($"| Next-turn stars | {cards.Count(card => card.StarNextTurn > 0)} |");
        writer.WriteLine($"| Draw | {cards.Count(card => card.Draw > 0)} |");
        writer.WriteLine($"| Next-turn draw | {cards.Count(card => card.DrawNextTurn > 0)} |");
        writer.WriteLine($"| Energy gain | {cards.Count(card => card.EnergyGain > 0)} |");
        writer.WriteLine($"| Next-turn energy | {cards.Count(card => card.EnergyNextTurn > 0)} |");
        writer.WriteLine($"| Forge | {cards.Count(card => card.Forge > 0)} |");
        writer.WriteLine($"| Vulnerable | {cards.Count(card => card.Vulnerable > 0)} |");
        writer.WriteLine();
        writer.WriteLine("## Resource Cards");
        writer.WriteLine();
        writer.WriteLine("| Card | Cost | Stars | Value | Draw | Draw next | Energy | Energy next | Star gain | Star next | Forge | Vulnerable | Warnings |");
        writer.WriteLine("| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");
        foreach (SimulationCard card in cards
            .Where(card => card.HasSimulatedResourceEffect)
            .OrderBy(card => card.TypeName, StringComparer.Ordinal))
        {
            writer.WriteLine(
                $"| {Escape(card.TypeName)} | {card.EnergyCost} | {card.StarCost} | {card.IntrinsicValue:0.###} | {card.Draw} | {card.DrawNextTurn} | {card.EnergyGain} | {card.EnergyNextTurn} | {card.StarGain} | {card.StarNextTurn} | {card.Forge} | {card.Vulnerable} | {card.Warnings.Count} |");
        }
    }

    private static void WriteDeckSimulationMarkdown(string path, DeckSimulationReport report)
    {
        using StreamWriter writer = new(path);
        writer.WriteLine("# Deck Simulation");
        writer.WriteLine();
        writer.WriteLine($"Provenance: {report.Provenance}");
        writer.WriteLine($"Deck size: {report.DeckSize}");
        writer.WriteLine($"Playable cards: {report.PlayableDeckSize}");
        writer.WriteLine($"Runs: {report.Options.Runs}");
        writer.WriteLine($"Turns: {report.Options.Turns}");
        writer.WriteLine($"Seed: {report.Options.Seed}");
        writer.WriteLine($"Total EV: {report.TotalExpectedValue:0.###}");
        writer.WriteLine($"Total variance: {report.TotalVariance:0.###}");
        writer.WriteLine();

        if (report.Warnings.Count > 0)
        {
            writer.WriteLine("## Warnings");
            writer.WriteLine();
            foreach (string warning in report.Warnings)
            {
                writer.WriteLine($"- {warning}");
            }

            writer.WriteLine();
        }

        writer.WriteLine("## Turns");
        writer.WriteLine();
        writer.WriteLine("| Turn | EV | Var | P10 | P50 | P90 | Drawn | Played | Energy spent | Energy wasted | Stars spent | Stars wasted | Unplayed value |");
        writer.WriteLine("| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");
        foreach (TurnSimulationSummary turn in report.Turns)
        {
            writer.WriteLine(
                $"| {turn.Turn} | {turn.ExpectedValue:0.###} | {turn.Variance:0.###} | {turn.P10:0.###} | {turn.P50:0.###} | {turn.P90:0.###} | {turn.AverageCardsDrawn:0.###} | {turn.AverageCardsPlayed:0.###} | {turn.AverageEnergySpent:0.###} | {turn.AverageEnergyWasted:0.###} | {turn.AverageStarsSpent:0.###} | {turn.AverageStarsWasted:0.###} | {turn.AverageUnplayedIntrinsicValue:0.###} |");
        }

        writer.WriteLine();
        writer.WriteLine("## Marginals");
        writer.WriteLine();
        writer.WriteLine("| Variant | EV delta | Per turn | Description |");
        writer.WriteLine("| --- | ---: | ---: | --- |");
        foreach (ResourceMarginalEstimate marginal in report.MarginalEstimates)
        {
            writer.WriteLine($"| {Escape(marginal.Name)} | {marginal.ExpectedValueDelta:0.###} | {marginal.PerTurnDelta:0.###} | {Escape(marginal.Description)} |");
        }

        writer.WriteLine();
        writer.WriteLine("## Most Played Cards");
        writer.WriteLine();
        writer.WriteLine("| Card | Plays | Plays/run | Value/play |");
        writer.WriteLine("| --- | ---: | ---: | ---: |");
        foreach (CardPlaySummary card in report.PlayedCards.Take(40))
        {
            writer.WriteLine($"| {Escape(card.TypeName)} | {card.PlayCount} | {card.AveragePlaysPerRun:0.###} | {card.AverageValuePerPlay:0.###} |");
        }
    }

    private static void WriteEnemyExpectationMarkdown(string path, IReadOnlyList<EnemyExpectationProfile> profiles)
    {
        using StreamWriter writer = new(path);
        writer.WriteLine("# Enemy Expectations");
        writer.WriteLine();
        writer.WriteLine("Damage basis: Ascension 10. Base damage is retained only as reference.");
        writer.WriteLine();
        writer.WriteLine("| Enemy | HP | Damage/move (Asc10) | Base damage/move | Attack rate | Block/move | Weak/move | Frail/move | Vuln/move | Moves | Warnings |");
        writer.WriteLine("| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");
        foreach (EnemyExpectationProfile profile in profiles
            .OrderByDescending(item => item.AscensionAverageDamagePerMove ?? item.AverageDamagePerMove)
            .ThenBy(item => item.TypeName, StringComparer.Ordinal))
        {
            string hp = profile.MinHp.HasValue && profile.MaxHp.HasValue
                ? $"{profile.MinHp:0.###}-{profile.MaxHp:0.###}"
                : "";
            decimal damage = profile.AscensionAverageDamagePerMove ?? profile.AverageDamagePerMove;

            writer.WriteLine(
                $"| {Escape(profile.TypeName)} | {hp} | {damage:0.###} | {profile.AverageDamagePerMove:0.###} | {profile.AttackMoveRate:0.###} | {profile.AverageBlockPerMove:0.###} | {profile.ExpectedWeakPerMove:0.###} | {profile.ExpectedFrailPerMove:0.###} | {profile.ExpectedVulnerablePerMove:0.###} | {profile.MoveCount} | {profile.Warnings.Count} |");
        }
    }

    private static void WriteEncounterPatternMarkdown(string path, IReadOnlyList<EncounterPatternEntry> entries)
    {
        using StreamWriter writer = new(path);
        writer.WriteLine("# Encounter Patterns");
        writer.WriteLine();
        writer.WriteLine("| Category | Count | Needs review | Conditional |");
        writer.WriteLine("| --- | ---: | ---: | ---: |");
        foreach (IGrouping<string, EncounterPatternEntry> group in entries.GroupBy(entry => entry.Category).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            writer.WriteLine($"| {Escape(group.Key)} | {group.Count()} | {group.Count(entry => entry.Warnings.Count > 0 || entry.Confidence < 0.7)} | {group.Count(entry => entry.HasConditionalMonsterSelection)} |");
        }

        writer.WriteLine();
        writer.WriteLine("## Patterns");
        writer.WriteLine();
        writer.WriteLine("| Act | Category | Encounter | Count | Monsters | Conditional | Confidence | Warnings |");
        writer.WriteLine("| --- | --- | --- | ---: | --- | --- | ---: | ---: |");
        foreach (EncounterPatternEntry entry in entries
            .OrderBy(entry => entry.Acts.FirstOrDefault()?.ActIndex ?? 99)
            .ThenBy(entry => entry.Acts.FirstOrDefault()?.ActTypeName ?? "", StringComparer.Ordinal)
            .ThenBy(entry => CategoryOrder(entry.Category))
            .ThenBy(entry => entry.TypeName, StringComparer.Ordinal))
        {
            string acts = entry.Acts.Count == 0
                ? ""
                : string.Join(", ", entry.Acts.Select(act => $"{act.ActTypeName}({act.ActNumber})"));
            writer.WriteLine(
                $"| {Escape(acts)} | {Escape(entry.Category)} | {Escape(entry.TypeName)} | {entry.FixedMonsterCount?.ToString() ?? ""} | {Escape(DescribeSlots(entry.MonsterSlots))} | {(entry.HasConditionalMonsterSelection ? "yes" : "no")} | {entry.Confidence:0.###} | {entry.Warnings.Count} |");
        }

        IReadOnlyList<EncounterPatternEntry> reviewEntries = entries
            .Where(entry => entry.Warnings.Count > 0 || entry.Confidence < 0.7)
            .OrderBy(entry => CategoryOrder(entry.Category))
            .ThenBy(entry => entry.TypeName, StringComparer.Ordinal)
            .ToArray();
        if (reviewEntries.Count == 0)
        {
            return;
        }

        writer.WriteLine();
        writer.WriteLine("## Needs Review");
        writer.WriteLine();
        foreach (EncounterPatternEntry entry in reviewEntries)
        {
            writer.WriteLine($"### {entry.TypeName}");
            writer.WriteLine();
            writer.WriteLine($"- Category: {entry.Category}");
            writer.WriteLine($"- Count: {entry.FixedMonsterCount?.ToString() ?? "variable/unknown"}");
            writer.WriteLine($"- Monsters: {DescribeSlots(entry.MonsterSlots)}");
            writer.WriteLine($"- Confidence: {entry.Confidence:0.###}");
            foreach (string warning in entry.Warnings)
            {
                writer.WriteLine($"- Warning: {warning}");
            }

            writer.WriteLine();
        }
    }

    private static int CategoryOrder(string category)
    {
        return category switch
        {
            "Weak" => 0,
            "Normal" => 1,
            "Elite" => 2,
            "Boss" => 3,
            "Event" => 4,
            "Debug" => 5,
            _ => 99
        };
    }

    private static string DescribeSlots(IReadOnlyList<EncounterMonsterSlot> slots)
    {
        if (slots.Count == 0)
        {
            return "";
        }

        return string.Join(" + ", slots.Select(slot =>
        {
            if (!string.IsNullOrWhiteSpace(slot.MonsterTypeName))
            {
                return slot.MonsterTypeName;
            }

            return slot.PossibleMonsterTypeNames.Count == 0
                ? "[unknown]"
                : "[" + string.Join("/", slot.PossibleMonsterTypeNames) + "]";
        }));
    }

    private static void WriteDefenseCalibrationMarkdown(string path, DefenseCalibrationReport report)
    {
        using StreamWriter writer = new(path);
        writer.WriteLine("# Defense Calibration Report");
        writer.WriteLine();
        writer.WriteLine("Damage basis: Ascension 10.");
        writer.WriteLine();
        writer.WriteLine("| Metric | Value |");
        writer.WriteLine("| --- | ---: |");
        writer.WriteLine($"| Enemies | {report.EnemyCount} |");
        writer.WriteLine($"| Needs review | {report.NeedsReviewCount} |");
        writer.WriteLine($"| Average damage / move (Asc10) | {report.AverageDamagePerMove:0.###} |");
        writer.WriteLine($"| Median damage / move | {report.MedianDamagePerMove:0.###} |");
        writer.WriteLine($"| P75 damage / move | {report.P75DamagePerMove:0.###} |");
        writer.WriteLine($"| P90 damage / move | {report.P90DamagePerMove:0.###} |");
        writer.WriteLine($"| Max average damage / move | {report.MaxDamagePerMove:0.###} |");
        writer.WriteLine($"| Average attack move rate | {report.AverageAttackMoveRate:0.###} |");
        writer.WriteLine($"| Weak / move | {report.AverageWeakPerMove:0.###} |");
        writer.WriteLine($"| Vulnerable / move | {report.AverageVulnerablePerMove:0.###} |");
        writer.WriteLine($"| Frail / move | {report.AverageFrailPerMove:0.###} |");
        writer.WriteLine($"| Strength gain / move | {report.AverageStrengthGainPerMove:0.###} |");
        writer.WriteLine();

        writer.WriteLine("## Fight Expectations");
        writer.WriteLine();
        writer.WriteLine("| Fight | Turns | Damage (Asc10) | Weak | Vulnerable | Frail | Strength gain |");
        writer.WriteLine("| --- | ---: | ---: | ---: | ---: | ---: | ---: |");
        foreach (FightDefenseExpectation fight in report.FightExpectations)
        {
            writer.WriteLine($"| {Escape(fight.FightType)} | {fight.ExpectedTurns:0.###} | {fight.ExpectedDamage:0.###} | {fight.ExpectedWeak:0.###} | {fight.ExpectedVulnerable:0.###} | {fight.ExpectedFrail:0.###} | {fight.ExpectedStrengthGain:0.###} |");
        }

        writer.WriteLine();
        writer.WriteLine("## Layer Pressures");
        writer.WriteLine();
        writer.WriteLine("| Layer | Pressure source | Weighted pressure | Block-to-damage | Candidate value / block | Required block / turn |");
        writer.WriteLine("| ---: | --- | ---: | ---: | ---: | ---: |");
        foreach (LayerDefensePressure layer in report.LayerPressures)
        {
            writer.WriteLine($"| {layer.Layer} | {Escape(layer.PressureSource)} | {layer.EffectiveDamagePerMove:0.###} | {layer.CurrentBlockToDamage:0.###} | {layer.CandidateValuePerBlock:0.###} | {layer.RequiredBlockPerMoveAtCurrentConversion:0.###} |");
        }

        if (report.Warnings.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine("## Warnings");
            writer.WriteLine();
            foreach (string warning in report.Warnings)
            {
                writer.WriteLine($"- {warning}");
            }
        }
    }

    private static void WriteEncounterWeightedEnemyPressureMarkdown(
        string path,
        EncounterWeightedEnemyPressureReport report)
    {
        using StreamWriter writer = new(path);
        writer.WriteLine("# Encounter-Weighted Enemy Pressure");
        writer.WriteLine();
        writer.WriteLine("Damage basis: Ascension 10");
        writer.WriteLine($"Opening window: T1-T{report.OpeningTurnCount}");
        writer.WriteLine($"Sustain window: T{report.SustainStartTurn}-T{report.SustainEndTurn}");
        writer.WriteLine($"Peak window: T1-T{report.TurnCount}");
        writer.WriteLine("Weighted pressure: Weak/Normal = 0.75 * Opening/turn + 0.25 * Sustain/turn; Elite = 0.55 * Opening/turn + 0.35 * Sustain/turn + 0.10 * Peak; Boss = 0.40 * Opening/turn + 0.40 * Sustain/turn + 0.20 * Peak.");
        writer.WriteLine();

        writer.WriteLine("## Layer Rules");
        writer.WriteLine();
        writer.WriteLine("Base map rooms are from ActModel.GetNumberOfRooms(false); they exclude the boss and Ancient/second-boss tail floors.");
        writer.WriteLine();
        writer.WriteLine("| Act | Acts | Layers | Base map rooms | Weak layers | Boss layers | Ancient layer | Game weak encounters |");
        writer.WriteLine("| ---: | --- | --- | ---: | --- | ---: | ---: | ---: |");
        foreach (EncounterLayerRule rule in report.LayerRules)
        {
            string bossLayers = FormatLayerRange(rule.BossStartLayer, rule.BossEndLayer);
            string ancientLayer = rule.AncientLayer?.ToString() ?? "";
            writer.WriteLine(
                $"| {rule.ActNumber} | {Escape(string.Join(", ", rule.ActTypeNames))} | {rule.StartLayer}-{rule.EndLayer} | {rule.BaseNumberOfRooms} | {rule.StartLayer}-{rule.WeakEndLayer} | {bossLayers} | {ancientLayer} | {rule.NumberOfWeakEncounters} |");
        }

        writer.WriteLine();
        writer.WriteLine("## Layer Segments");
        writer.WriteLine();
        writer.WriteLine("| Layers | Segment | Categories | Encounters | Needs review | Weighted pressure | Opening T1-3 | Opening/turn | Sustain T4-8 | Sustain/turn | Peak T1-8 | Scaling delta/turn |");
        writer.WriteLine("| --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");
        foreach (EncounterLayerPressureSegment segment in report.LayerSegments)
        {
            writer.WriteLine(
                $"| {segment.StartLayer}-{segment.EndLayer} | {Escape(segment.ActLabel + " " + segment.SegmentKind)} | {Escape(string.Join("+", segment.IncludedCategories))} | {segment.EncounterCount} | {segment.NeedsReviewCount} | {segment.AverageWeightedPressure:0.###} | {segment.AverageOpeningDamage:0.###} | {segment.AverageOpeningDamagePerTurn:0.###} | {segment.AverageSustainDamage:0.###} | {segment.AverageSustainDamagePerTurn:0.###} | {segment.AveragePeakDamage:0.###} | {segment.AverageScalingDeltaPerTurn:0.###} |");
        }

        writer.WriteLine();
        writer.WriteLine("## Encounter Damage");
        writer.WriteLine();
        writer.WriteLine("| Act | Category | Encounter | Monsters | Weighted pressure | Opening T1-3 | Sustain T4-8 | Peak T1-8 | Scaling delta/turn | Turns T1-8 | Conditional | Confidence | Warnings |");
        writer.WriteLine("| --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | --- | --- | ---: | ---: |");
        foreach (EncounterDamageProfile encounter in report.Encounters)
        {
            string acts = encounter.ActNumbers.Count == 0
                ? ""
                : string.Join(",", encounter.ActNumbers);
            writer.WriteLine(
                $"| {Escape(acts)} | {Escape(encounter.Category)} | {Escape(encounter.TypeName)} | {encounter.MonsterSlotCount} | {encounter.WeightedPressure:0.###} | {encounter.OpeningDamage:0.###} | {encounter.SustainDamage:0.###} | {encounter.PeakDamage:0.###} | {encounter.ScalingDeltaPerTurn:0.###} | {Escape(string.Join("/", encounter.TurnDamages.Select(value => value.ToString("0.###"))))} | {(encounter.HasConditionalMonsterSelection ? "yes" : "no")} | {encounter.Confidence:0.###} | {encounter.Warnings.Count} |");
        }

        if (report.Warnings.Count == 0)
        {
            return;
        }

        writer.WriteLine();
        writer.WriteLine("## Warnings");
        writer.WriteLine();
        foreach (string warning in report.Warnings)
        {
            writer.WriteLine($"- {warning}");
        }
    }

    private static string FormatLayerRange(int startLayer, int endLayer)
    {
        return startLayer == endLayer
            ? startLayer.ToString()
            : $"{startLayer}-{endLayer}";
    }
}
