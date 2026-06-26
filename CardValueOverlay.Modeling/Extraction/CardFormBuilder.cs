namespace CardValueOverlay.Modeling.Extraction;

public sealed class CardFormBuilder
{
    public CardForm Build(CardFactCatalogEntry entry, int upgradeLevel)
    {
        if (upgradeLevel < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(upgradeLevel), "Upgrade level must be non-negative.");
        }

        bool upgraded = upgradeLevel > 0;
        int? cost = entry.Cost;
        HashSet<string> keywords = entry.Keywords.ToHashSet(StringComparer.Ordinal);
        List<string> unresolved = [.. entry.Unresolved];

        if (upgraded)
        {
            foreach (UpgradeOperationFact operation in entry.UpgradeOperations)
            {
                switch (operation.Kind)
                {
                    case "upgradeCost":
                        if (cost.HasValue && operation.Amount.HasValue)
                        {
                            cost += (int)Math.Round(operation.Amount.Value, MidpointRounding.AwayFromZero);
                        }
                        break;
                    case "addKeyword":
                        keywords.Add(operation.Name);
                        break;
                    case "removeKeyword":
                        keywords.Remove(operation.Name);
                        break;
                    case "upgradeGeneratedCards":
                        unresolved.Add("Upgraded form upgrades generated/selected cards; downstream consumers must model this action explicitly.");
                        break;
                }
            }
        }

        IReadOnlyList<CardActionFact> actions = upgraded
            ? entry.Actions.Select(action => ApplyUpgradeOperations(action, entry.UpgradeOperations)).ToArray()
            : entry.Actions;

        return new CardForm(
            entry.ModelId,
            entry.TypeName,
            entry.FullTypeName,
            upgradeLevel,
            cost,
            entry.CardType,
            entry.Rarity,
            entry.TargetType,
            keywords.Order(StringComparer.Ordinal).ToArray(),
            entry.Tags,
            actions,
            unresolved.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            $"{entry.Provenance}; card form upgradeLevel {upgradeLevel}",
            entry.Confidence);
    }

    private static CardActionFact ApplyUpgradeOperations(
        CardActionFact action,
        IReadOnlyList<UpgradeOperationFact> operations)
    {
        if (action.DynamicVarName is null || action.Amount is null)
        {
            return action;
        }

        decimal delta = operations
            .Where(operation => operation.Kind == "upgradeDynamicVar")
            .Where(operation => string.Equals(operation.Name, action.DynamicVarName, StringComparison.OrdinalIgnoreCase))
            .Sum(operation => operation.Amount ?? 0m);
        return delta == 0m ? action : action with { Amount = action.Amount + delta };
    }
}

