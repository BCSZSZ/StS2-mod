namespace CardValueOverlay.Modeling.Combat;

public enum CombatSolveMode
{
    Exact,
    Sparse
}

public sealed record CombatSimulationOptions
{
    public int SemanticsVersion { get; init; } = 7;

    public int Ascension { get; init; } = 10;

    public int HorizonTurns { get; init; } = 4;

    public int HandSize { get; init; } = 5;

    public int MaxHandSize { get; init; } = 10;

    public int EnergyPerTurn { get; init; } = 3;

    public int InitialStars { get; init; }

    public int MaximumCanonicalStates { get; init; } = 250_000;

    public long MaximumChanceBranches { get; init; } = 2_000_000;

    public int ExactOutcomeLimit { get; init; } = 100_000;

    public int MaximumDecisionDepth { get; init; } = 64;

    public int SparseChanceSamples { get; init; } = 64;

    public CombatSolveMode SolveMode { get; init; } = CombatSolveMode.Exact;

    public bool EnableMemoization { get; init; } = true;

    public void Validate()
    {
        if (SemanticsVersion != 7)
        {
            throw new InvalidOperationException("Unsupported combat semantics version.");
        }

        if (Ascension != 10)
        {
            throw new InvalidOperationException("Phase 1 combat modeling is A10-only.");
        }

        if (HorizonTurns <= 0 || HandSize <= 0 || MaxHandSize < HandSize ||
            EnergyPerTurn < 0 || InitialStars < 0)
        {
            throw new InvalidOperationException("Invalid combat horizon, hand-size, or energy options.");
        }

        if (MaximumCanonicalStates <= 0 || MaximumChanceBranches <= 0 || ExactOutcomeLimit <= 0 ||
            MaximumDecisionDepth <= 0 || SparseChanceSamples <= 0)
        {
            throw new InvalidOperationException("Solver budgets must be positive.");
        }
    }
}
