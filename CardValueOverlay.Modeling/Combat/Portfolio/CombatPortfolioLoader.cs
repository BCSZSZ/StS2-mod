using System.Text.Json;

namespace CardValueOverlay.Modeling.Combat.Portfolio;

public static class CombatPortfolioRules
{
    public static IReadOnlyList<(int Act, string Tier, int Budget)> ExpectedCells { get; } =
    [
        (1, "weak", 0), (1, "normal", 8), (1, "elite", 15), (1, "boss", 30),
        (2, "weak", 5), (2, "normal", 10), (2, "elite", 20), (2, "boss", 40),
        (3, "weak", 10), (3, "normal", 15), (3, "elite", 30), (3, "boss", 40)
    ];
}

public sealed record LoadedCombatPortfolio(
    CombatPortfolioDefinition Definition,
    string ContentHash,
    IReadOnlyList<string> Warnings);

public static class CombatPortfolioLoader
{
    public static LoadedCombatPortfolio Load(
        string path,
        HpContinuationCatalog hpCatalog,
        string? portfolioId = null)
    {
        CombatPortfolioDefinition portfolio = JsonSerializer.Deserialize<CombatPortfolioDefinition>(
            File.ReadAllText(path),
            CombatJson.Options)
            ?? throw new InvalidOperationException($"Combat portfolio file '{path}' is empty.");
        IReadOnlyList<string> warnings = Validate(portfolio, hpCatalog, portfolioId);
        return new LoadedCombatPortfolio(portfolio, CombatJson.Sha256File(path), warnings);
    }

    public static IReadOnlyList<string> Validate(
        CombatPortfolioDefinition portfolio,
        HpContinuationCatalog hpCatalog,
        string? expectedPortfolioId = null)
    {
        if (portfolio.SchemaVersion != 1 || portfolio.Ascension != 10)
        {
            throw new InvalidOperationException("Combat portfolio must use schemaVersion 1 and Ascension 10.");
        }

        if (expectedPortfolioId is not null && !string.Equals(expectedPortfolioId, portfolio.PortfolioId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Expected portfolio '{expectedPortfolioId}', found '{portfolio.PortfolioId}'.");
        }

        if (!string.Equals(portfolio.Status, "research", StringComparison.Ordinal) ||
            !string.Equals(portfolio.TargetWeightStatus, "prior", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Phase 1 portfolio must remain research with prior target weights.");
        }

        if (portfolio.Cells.Count != 12 || portfolio.Cells.Select(cell => cell.Id).Distinct(StringComparer.Ordinal).Count() != 12)
        {
            throw new InvalidOperationException("Combat portfolio must define twelve unique cells.");
        }

        foreach ((int act, string tier, _) in CombatPortfolioRules.ExpectedCells)
        {
            CombatPortfolioCell? cell = portfolio.Cells.SingleOrDefault(candidate =>
                candidate.Act == act && string.Equals(candidate.Tier, tier, StringComparison.OrdinalIgnoreCase));
            if (cell is null)
            {
                throw new InvalidOperationException($"Combat portfolio is missing act {act}/{tier}.");
            }

            HpContinuationContext context = hpCatalog.Get(cell.HpContextId);
            if (context.Act != act || !string.Equals(context.Tier, tier, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Cell '{cell.Id}' references a mismatched HP context.");
            }

            if (cell.ProposalSamples <= 0 || cell.TargetWeight < 0 || cell.DeckGroups.Count == 0 ||
                cell.EncounterSelectors.Count == 0 || cell.MinimumUniqueEncounters <= 0 ||
                cell.StartHp.Values.Count == 0 || cell.StartHp.MaxHp <= 0)
            {
                throw new InvalidOperationException($"Cell '{cell.Id}' has an empty or invalid selector/allocation.");
            }

            foreach (string selector in cell.EncounterSelectors)
            {
                if (!IsValidEncounterSelector(selector))
                {
                    throw new InvalidOperationException($"Cell '{cell.Id}' has invalid encounter selector '{selector}'.");
                }
            }

            if (cell.StartHp.Values.Any(value => value <= 0 || value > cell.StartHp.MaxHp))
            {
                throw new InvalidOperationException($"Cell '{cell.Id}' contains an invalid start HP value.");
            }
        }

        double weightSum = portfolio.Cells.Sum(cell => cell.TargetWeight);
        if (Math.Abs(weightSum - 1d) > 1e-9)
        {
            throw new InvalidOperationException($"Combat portfolio target weights must sum to 1, found {weightSum:R}.");
        }

        return
        [
            "Target weights are research priors, not empirical route exposure weights.",
            "HP parameters are sensitivity priors and cannot produce runtimeCandidate=true."
        ];
    }

    private static bool IsValidEncounterSelector(string selector) =>
        selector.StartsWith("category:", StringComparison.OrdinalIgnoreCase) ||
        selector.StartsWith("modelId:", StringComparison.OrdinalIgnoreCase) ||
        selector.StartsWith("typeName:", StringComparison.OrdinalIgnoreCase) ||
        selector.StartsWith("stableId:", StringComparison.OrdinalIgnoreCase);
}
