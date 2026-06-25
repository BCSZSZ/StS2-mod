using CardValueOverlay.Modeling.Extraction;

namespace CardValueOverlay.Modeling.Estimation;

public sealed class EncounterWeightedEnemyPressureEstimator
{
    private const int DefaultTurnCount = 8;
    private const int OpeningTurnCount = 3;
    private const int SustainStartTurn = 4;
    private const int SustainEndTurn = 8;

    public EncounterWeightedEnemyPressureReport Estimate(
        IReadOnlyList<MonsterMoveProfileEntry> monsterProfiles,
        IReadOnlyList<EncounterPatternEntry> encounterPatterns,
        int turnCount = DefaultTurnCount)
    {
        if (turnCount < SustainEndTurn)
        {
            throw new ArgumentOutOfRangeException(
                nameof(turnCount),
                $"turnCount must be at least {SustainEndTurn} for opening, sustain, and peak pressure windows.");
        }

        Dictionary<string, MonsterMoveProfileEntry> monstersByType = monsterProfiles.ToDictionary(
            profile => profile.TypeName,
            StringComparer.Ordinal);
        IReadOnlyList<EncounterDamageProfile> encounterDamage = encounterPatterns
            .Select(pattern => EstimateEncounter(pattern, monstersByType, turnCount))
            .OrderBy(profile => profile.ActNumbers.FirstOrDefault(99))
            .ThenBy(profile => CategoryOrder(profile.Category))
            .ThenBy(profile => profile.TypeName, StringComparer.Ordinal)
            .ToArray();
        IReadOnlyList<EncounterLayerRule> rules = BuildLayerRules(encounterPatterns);
        IReadOnlyList<EncounterLayerPressureSegment> segments = BuildLayerSegments(rules, encounterDamage);

        List<string> warnings = [];
        int encounterNeedsReview = encounterDamage.Count(profile => profile.Warnings.Count > 0 || profile.Confidence < 0.7);
        if (encounterNeedsReview > 0)
        {
            warnings.Add($"{encounterNeedsReview} encounter damage profiles need review before using this as final calibration.");
        }

        warnings.AddRange(segments.SelectMany(segment => segment.Warnings.Select(warning => $"{segment.ActLabel} {segment.SegmentKind}: {warning}")));

        return new EncounterWeightedEnemyPressureReport(
            turnCount,
            OpeningTurnCount,
            SustainStartTurn,
            SustainEndTurn,
            rules,
            segments,
            encounterDamage,
            warnings.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            "encounter_patterns.generated.json + monster_move_profiles.generated.json asc10 opening/sustain encounter-weighted estimator v1");
    }

    private static EncounterDamageProfile EstimateEncounter(
        EncounterPatternEntry pattern,
        IReadOnlyDictionary<string, MonsterMoveProfileEntry> monstersByType,
        int turnCount)
    {
        decimal[] ascensionTurnDamages = new decimal[turnCount];
        List<string> warnings = [.. pattern.Warnings];
        double confidence = pattern.Confidence;

        foreach (EncounterMonsterSlot slot in pattern.MonsterSlots)
        {
            IReadOnlyList<string> possibleTypes = GetPossibleMonsterTypes(slot);
            if (possibleTypes.Count == 0)
            {
                warnings.Add($"Slot {slot.Position} has no parsed monster type.");
                confidence = Math.Min(confidence, 0.2);
                continue;
            }

            List<MonsterTurnDamage> possibleDamages = [];
            foreach (string typeName in possibleTypes)
            {
                if (!monstersByType.TryGetValue(typeName, out MonsterMoveProfileEntry? monster))
                {
                    warnings.Add($"Monster profile was not found for {typeName}.");
                    confidence = Math.Min(confidence, 0.1);
                    continue;
                }

                MonsterTurnDamage monsterDamage = EstimateMonster(monster, turnCount);
                possibleDamages.Add(monsterDamage);
                confidence = Math.Min(confidence, monsterDamage.Confidence);
                warnings.AddRange(monsterDamage.Warnings.Select(warning => $"{typeName}: {warning}"));
            }

            if (possibleDamages.Count == 0)
            {
                continue;
            }

            for (int i = 0; i < turnCount; i++)
            {
                ascensionTurnDamages[i] += Average(possibleDamages, damage => damage.AscensionTurnDamages[i]);
            }
        }

        IReadOnlyList<decimal> primaryTurnDamages = ascensionTurnDamages.Select(Round).ToArray();
        decimal openingDamage = SumTurns(primaryTurnDamages, 1, OpeningTurnCount);
        decimal sustainDamage = SumTurns(primaryTurnDamages, SustainStartTurn, SustainEndTurn);
        decimal openingDamagePerTurn = Round(openingDamage / OpeningTurnCount);
        decimal sustainDamagePerTurn = Round(sustainDamage / (SustainEndTurn - SustainStartTurn + 1));
        decimal peakDamage = primaryTurnDamages.Count == 0 ? 0m : Round(primaryTurnDamages.Max());

        return new EncounterDamageProfile(
            pattern.ModelId,
            pattern.TypeName,
            pattern.Acts.Select(act => act.ActTypeName).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            pattern.Acts.Select(act => act.ActNumber).Distinct().Order().ToArray(),
            pattern.Category,
            turnCount,
            openingDamage,
            openingDamagePerTurn,
            sustainDamage,
            sustainDamagePerTurn,
            peakDamage,
            Round(sustainDamagePerTurn - openingDamagePerTurn),
            CalculateWeightedPressure(pattern.Category, openingDamagePerTurn, sustainDamagePerTurn, peakDamage),
            primaryTurnDamages,
            pattern.MonsterSlots.Count,
            pattern.HasConditionalMonsterSelection,
            Math.Round(confidence, 3),
            warnings.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
    }

    private static MonsterTurnDamage EstimateMonster(MonsterMoveProfileEntry profile, int turnCount)
    {
        List<string> warnings = [.. profile.Unresolved];
        if (profile.Moves.Count == 0)
        {
            warnings.Add("Monster has no parsed moves.");
            return new MonsterTurnDamage(
                Enumerable.Repeat(0m, turnCount).ToArray(),
                Enumerable.Repeat(0m, turnCount).ToArray(),
                0.0,
                warnings.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
        }

        Dictionary<string, MonsterMoveStateEntry> movesByState = profile.Moves.ToDictionary(move => move.StateId, StringComparer.Ordinal);
        IReadOnlyList<string> currentStates = profile.InitialStateId is not null && movesByState.ContainsKey(profile.InitialStateId)
            ? [profile.InitialStateId]
            : profile.Moves.Select(move => move.StateId).ToArray();

        if (profile.InitialStateId is null || !movesByState.ContainsKey(profile.InitialStateId))
        {
            warnings.Add("Initial state is unknown; turn simulation starts from equal-weight moves.");
        }

        decimal[] turnDamages = new decimal[turnCount];
        decimal[] ascensionTurnDamages = new decimal[turnCount];
        double confidence = profile.Confidence;

        for (int turn = 0; turn < turnCount; turn++)
        {
            List<MoveDamage> moveDamages = [];
            List<string> nextStates = [];
            foreach (string stateId in currentStates)
            {
                if (!movesByState.TryGetValue(stateId, out MonsterMoveStateEntry? move))
                {
                    continue;
                }

                MoveDamage moveDamage = EstimateMoveDamage(move);
                moveDamages.Add(moveDamage);
                confidence = Math.Min(confidence, moveDamage.Confidence);
                warnings.AddRange(moveDamage.Warnings.Select(warning => $"{move.StateId}: {warning}"));

                if (move.FollowUpStateIds.Count > 0)
                {
                    nextStates.AddRange(move.FollowUpStateIds.Where(movesByState.ContainsKey));
                }
            }

            if (moveDamages.Count == 0)
            {
                warnings.Add($"No move damage could be estimated for turn {turn + 1}.");
            }
            else
            {
                turnDamages[turn] = Average(moveDamages, damage => damage.Damage);
                ascensionTurnDamages[turn] = Average(moveDamages, damage => damage.AscensionDamage);
            }

            currentStates = nextStates.Count > 0
                ? nextStates.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()
                : profile.Moves.Select(move => move.StateId).ToArray();

            if (nextStates.Count == 0 && profile.Moves.Count > 1)
            {
                warnings.Add($"Turn {turn + 1} has no parsed follow-up state; next turn falls back to equal-weight moves.");
            }
        }

        return new MonsterTurnDamage(
            turnDamages.Select(Round).ToArray(),
            ascensionTurnDamages.Select(Round).ToArray(),
            Math.Round(confidence, 3),
            warnings.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
    }

    private static MoveDamage EstimateMoveDamage(MonsterMoveStateEntry move)
    {
        decimal damage = 0m;
        decimal ascensionDamage = 0m;
        List<string> warnings = [.. move.Warnings];
        double confidence = move.Confidence;

        foreach (MonsterMoveEffectTerm effect in move.Effects.Where(effect => effect.Kind == "attack"))
        {
            decimal amount = effect.Amount?.Value ?? 0m;
            decimal ascensionAmount = effect.Amount?.AscensionValue ?? amount;
            decimal hitCount = effect.HitCount?.Value ?? 1m;
            decimal ascensionHitCount = effect.HitCount?.AscensionValue ?? hitCount;

            if (effect.Amount?.Value is null)
            {
                warnings.Add($"Attack effect has unknown amount from {effect.Source}.");
                confidence = Math.Min(confidence, 0.25);
            }

            damage += amount * hitCount;
            ascensionDamage += ascensionAmount * ascensionHitCount;
            confidence = Math.Min(confidence, effect.Confidence);
        }

        return new MoveDamage(
            damage,
            ascensionDamage,
            Math.Round(confidence, 3),
            warnings.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
    }

    private static IReadOnlyList<EncounterLayerRule> BuildLayerRules(IReadOnlyList<EncounterPatternEntry> patterns)
    {
        var acts = patterns
            .SelectMany(pattern => pattern.Acts)
            .GroupBy(act => act.ActNumber)
            .OrderBy(group => group.Key)
            .ToArray();

        List<EncounterLayerRule> rules = [];
        int startLayer = 1;
        foreach (IGrouping<int, EncounterActReference> group in acts)
        {
            int baseNumberOfRooms = group.Max(act => act.BaseNumberOfRooms);
            int totalFloors = baseNumberOfRooms + 2;
            int weakLayerCount = group.Key == 1 ? 5 : 3;
            int bossLayerCount = group.Key == 3 ? 2 : 1;
            int numberOfWeakEncounters = group.Max(act => act.NumberOfWeakEncounters);
            int endLayer = startLayer + totalFloors - 1;
            int weakEndLayer = Math.Min(startLayer + weakLayerCount - 1, startLayer + baseNumberOfRooms - 1);
            int bossEndLayer = endLayer;
            int bossStartLayer = bossEndLayer - bossLayerCount + 1;
            int? ancientLayer = group.Key == 3 ? null : endLayer;
            if (ancientLayer.HasValue)
            {
                bossStartLayer = ancientLayer.Value - bossLayerCount;
                bossEndLayer = ancientLayer.Value - 1;
            }

            rules.Add(new EncounterLayerRule(
                group.Key,
                group.Select(act => act.ActTypeName).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
                startLayer,
                baseNumberOfRooms,
                totalFloors,
                weakEndLayer,
                bossStartLayer,
                bossEndLayer,
                ancientLayer,
                endLayer,
                numberOfWeakEncounters,
                weakLayerCount));

            startLayer = endLayer + 1;
        }

        return rules;
    }

    private static IReadOnlyList<EncounterLayerPressureSegment> BuildLayerSegments(
        IReadOnlyList<EncounterLayerRule> rules,
        IReadOnlyList<EncounterDamageProfile> encounters)
    {
        List<EncounterLayerPressureSegment> segments = [];
        foreach (EncounterLayerRule rule in rules)
        {
            AddSegment(
                segments,
                rule,
                rule.StartLayer,
                rule.WeakEndLayer,
                "weak",
                ["Weak"],
                encounters);

            AddSegment(
                segments,
                rule,
                rule.WeakEndLayer + 1,
                rule.BossStartLayer - 1,
                "normal+elite",
                ["Normal", "Elite"],
                encounters);

            AddSegment(
                segments,
                rule,
                rule.BossStartLayer,
                rule.BossEndLayer,
                "boss",
                ["Boss"],
                encounters);

            if (rule.AncientLayer.HasValue)
            {
                AddSegment(
                    segments,
                    rule,
                    rule.AncientLayer.Value,
                    rule.AncientLayer.Value,
                    "ancient/noncombat",
                    [],
                    encounters,
                    allowEmpty: true);
            }
        }

        return segments;
    }

    private static void AddSegment(
        List<EncounterLayerPressureSegment> segments,
        EncounterLayerRule rule,
        int startLayer,
        int endLayer,
        string segmentKind,
        IReadOnlyList<string> includedCategories,
        IReadOnlyList<EncounterDamageProfile> encounters,
        bool allowEmpty = false)
    {
        if (startLayer > endLayer)
        {
            return;
        }

        EncounterDamageProfile[] included = encounters
            .Where(encounter => encounter.ActNumbers.Contains(rule.ActNumber))
            .Where(encounter => encounter.ActTypeNames.Any(act => rule.ActTypeNames.Contains(act, StringComparer.Ordinal)))
            .Where(encounter => includedCategories.Contains(encounter.Category, StringComparer.Ordinal))
            .OrderBy(encounter => encounter.TypeName, StringComparer.Ordinal)
            .ToArray();
        List<string> warnings = [];
        if (included.Length == 0 && !allowEmpty)
        {
            warnings.Add("No encounters matched this layer segment.");
        }

        int needsReview = included.Count(encounter => encounter.Warnings.Count > 0 || encounter.Confidence < 0.7);
        if (needsReview > 0)
        {
            warnings.Add($"{needsReview} included encounters need review.");
        }

        segments.Add(new EncounterLayerPressureSegment(
            rule.ActNumber,
            $"Act {rule.ActNumber}: {string.Join("/", rule.ActTypeNames)}",
            startLayer,
            endLayer,
            segmentKind,
            includedCategories,
            included.Length,
            needsReview,
            Round(included.Length == 0 ? 0m : included.Average(encounter => encounter.OpeningDamage)),
            Round(included.Length == 0 ? 0m : included.Average(encounter => encounter.OpeningDamagePerTurn)),
            Round(included.Length == 0 ? 0m : included.Average(encounter => encounter.SustainDamage)),
            Round(included.Length == 0 ? 0m : included.Average(encounter => encounter.SustainDamagePerTurn)),
            Round(included.Length == 0 ? 0m : included.Average(encounter => encounter.PeakDamage)),
            Round(included.Length == 0 ? 0m : included.Average(encounter => encounter.ScalingDeltaPerTurn)),
            Round(included.Length == 0 ? 0m : included.Average(encounter => encounter.WeightedPressure)),
            included.Select(encounter => encounter.TypeName).ToArray(),
            warnings.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()));
    }

    private static decimal CalculateWeightedPressure(
        string category,
        decimal openingDamagePerTurn,
        decimal sustainDamagePerTurn,
        decimal peakDamage)
    {
        return category switch
        {
            "Weak" or "Normal" => Round((0.75m * openingDamagePerTurn) + (0.25m * sustainDamagePerTurn)),
            "Elite" => Round((0.55m * openingDamagePerTurn) + (0.35m * sustainDamagePerTurn) + (0.10m * peakDamage)),
            "Boss" => Round((0.40m * openingDamagePerTurn) + (0.40m * sustainDamagePerTurn) + (0.20m * peakDamage)),
            _ => 0m
        };
    }

    private static decimal SumTurns(IReadOnlyList<decimal> turnDamages, int startTurn, int endTurn)
    {
        return Round(turnDamages
            .Skip(startTurn - 1)
            .Take(endTurn - startTurn + 1)
            .Sum());
    }

    private static IReadOnlyList<string> GetPossibleMonsterTypes(EncounterMonsterSlot slot)
    {
        if (!string.IsNullOrWhiteSpace(slot.MonsterTypeName))
        {
            return [slot.MonsterTypeName];
        }

        return slot.PossibleMonsterTypeNames;
    }

    private static decimal Average<T>(IReadOnlyList<T> items, Func<T, decimal> selector)
    {
        return items.Count == 0 ? 0m : items.Average(selector);
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

    private static decimal Round(decimal value)
    {
        return Math.Round(value, 3, MidpointRounding.AwayFromZero);
    }

    private sealed record MoveDamage(
        decimal Damage,
        decimal AscensionDamage,
        double Confidence,
        IReadOnlyList<string> Warnings);

    private sealed record MonsterTurnDamage(
        IReadOnlyList<decimal> TurnDamages,
        IReadOnlyList<decimal> AscensionTurnDamages,
        double Confidence,
        IReadOnlyList<string> Warnings);
}
