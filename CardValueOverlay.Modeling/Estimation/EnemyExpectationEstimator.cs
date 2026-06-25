using CardValueOverlay.Modeling.Extraction;

namespace CardValueOverlay.Modeling.Estimation;

public sealed class EnemyExpectationEstimator
{
    public IReadOnlyList<EnemyExpectationProfile> Estimate(IReadOnlyList<MonsterMoveProfileEntry> profiles)
    {
        return profiles
            .Select(Estimate)
            .OrderBy(profile => profile.TypeName, StringComparer.Ordinal)
            .ToArray();
    }

    public EnemyExpectationProfile Estimate(MonsterMoveProfileEntry profile)
    {
        List<string> warnings = [.. profile.Unresolved];
        List<EnemyMoveExpectation> moves = profile.Moves.Select(EstimateMove).ToList();
        warnings.AddRange(profile.Moves.SelectMany(move => move.Warnings.Select(warning => $"{move.StateId}: {warning}")));

        if (profile.InitialStateId is null)
        {
            warnings.Add("Initial state is unknown or conditional; equal move weighting is a v1 approximation.");
        }

        if (moves.Count == 0)
        {
            warnings.Add("No move expectations were estimated.");
        }

        decimal totalWeight = moves.Sum(move => move.Weight);
        decimal Average(Func<EnemyMoveExpectation, decimal> selector)
        {
            return totalWeight == 0m ? 0m : moves.Sum(move => selector(move) * move.Weight) / totalWeight;
        }

        decimal? NullableAverage(Func<EnemyMoveExpectation, decimal?> selector)
        {
            if (totalWeight == 0m)
            {
                return null;
            }

            decimal knownWeight = moves.Where(move => selector(move).HasValue).Sum(move => move.Weight);
            if (knownWeight == 0m)
            {
                return null;
            }

            return moves
                .Where(move => selector(move).HasValue)
                .Sum(move => selector(move)!.Value * move.Weight) / knownWeight;
        }

        int parsedMoveCount = moves.Count(move => move.Warnings.Count == 0);
        double confidence = moves.Count == 0
            ? 0.0
            : Math.Min(profile.Confidence, moves.Min(move => move.Confidence));

        return new EnemyExpectationProfile(
            profile.ModelId,
            profile.TypeName,
            profile.FullTypeName,
            profile.HpRange?.Min?.Value,
            profile.HpRange?.Max?.Value,
            profile.HpRange?.Min?.AscensionValue,
            profile.HpRange?.Max?.AscensionValue,
            Round(Average(move => move.Damage)),
            NullableAverage(move => move.AscensionDamage) is { } ascDamage ? Round(ascDamage) : null,
            Round(moves.Count == 0 ? 0m : moves.Max(move => move.Damage)),
            Round(moves.Count == 0 ? 0m : moves.Count(move => move.Damage > 0m) / (decimal)moves.Count),
            Round(Average(move => move.Block)),
            NullableAverage(move => move.AscensionBlock) is { } ascBlock ? Round(ascBlock) : null,
            Round(Average(move => move.Weak)),
            Round(Average(move => move.Vulnerable)),
            Round(Average(move => move.Frail)),
            Round(Average(move => move.StrengthGain)),
            profile.Moves.Count,
            parsedMoveCount,
            Math.Round(confidence, 3),
            moves.Select(RoundMove).ToArray(),
            warnings.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            "monster_move_profiles.generated.json equal-weight expectation estimator v1");
    }

    private static EnemyMoveExpectation EstimateMove(MonsterMoveStateEntry move)
    {
        decimal damage = 0m;
        decimal? ascensionDamage = 0m;
        decimal block = 0m;
        decimal? ascensionBlock = 0m;
        decimal weak = 0m;
        decimal vulnerable = 0m;
        decimal frail = 0m;
        decimal strengthGain = 0m;
        decimal attackHitCount = 0m;
        List<string> warnings = [.. move.Warnings];

        foreach (MonsterMoveEffectTerm effect in move.Effects)
        {
            decimal amount = effect.Amount?.Value ?? 0m;
            decimal? ascensionAmount = effect.Amount?.AscensionValue;
            decimal hitCount = effect.HitCount?.Value ?? 1m;
            decimal? ascensionHitCount = effect.HitCount?.AscensionValue;

            if (effect.Amount is null || !effect.Amount.Value.HasValue)
            {
                warnings.Add($"Effect '{effect.Kind}' has unknown amount from {effect.Source}.");
            }

            switch (effect.Kind)
            {
                case "attack":
                    damage += amount * hitCount;
                    ascensionDamage = AddNullable(
                        ascensionDamage,
                        (ascensionAmount ?? amount) * (ascensionHitCount ?? hitCount));
                    attackHitCount += hitCount;
                    break;
                case "block":
                    block += amount;
                    ascensionBlock = AddNullable(ascensionBlock, ascensionAmount ?? amount);
                    break;
                case "debuffWeak":
                    weak += amount;
                    break;
                case "debuffVulnerable":
                    vulnerable += amount;
                    break;
                case "debuffFrail":
                    frail += amount;
                    break;
                case "buffStrength":
                    strengthGain += amount;
                    break;
                default:
                    warnings.Add($"Unsupported move effect '{effect.Kind}' from {effect.Source}.");
                    break;
            }
        }

        return new EnemyMoveExpectation(
            move.StateId,
            damage,
            ascensionDamage,
            block,
            ascensionBlock,
            weak,
            vulnerable,
            frail,
            strengthGain,
            attackHitCount,
            1m,
            move.Confidence,
            warnings.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
    }

    private static decimal? AddNullable(decimal? current, decimal value)
    {
        return (current ?? 0m) + value;
    }

    private static EnemyMoveExpectation RoundMove(EnemyMoveExpectation move)
    {
        return move with
        {
            Damage = Round(move.Damage),
            AscensionDamage = move.AscensionDamage.HasValue ? Round(move.AscensionDamage.Value) : null,
            Block = Round(move.Block),
            AscensionBlock = move.AscensionBlock.HasValue ? Round(move.AscensionBlock.Value) : null,
            Weak = Round(move.Weak),
            Vulnerable = Round(move.Vulnerable),
            Frail = Round(move.Frail),
            StrengthGain = Round(move.StrengthGain),
            AttackHitCount = Round(move.AttackHitCount),
            Weight = Round(move.Weight)
        };
    }

    private static decimal Round(decimal value)
    {
        return Math.Round(value, 3, MidpointRounding.AwayFromZero);
    }
}
