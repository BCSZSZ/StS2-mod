namespace CardValueOverlay.Modeling.Combat.Portfolio;

public sealed record CombatRiskSummary(
    double MeanLoss,
    double P90Loss,
    double CVar90Loss,
    double ProbabilityLossExceedsBudget,
    double DeathProbability);

public sealed record CombatCellDeltaReport(
    string CellId,
    int Act,
    string Tier,
    int Horizon,
    int LossBudget,
    double TargetWeight,
    double ProposalWeight,
    int SampleCount,
    double EffectiveSampleSize,
    double SupportedFraction,
    double DeltaEv,
    double ConfidenceLow,
    double ConfidenceHigh,
    CombatRiskSummary BaselineRisk,
    CombatRiskSummary CandidateRisk,
    CombatPhysicalMetrics BaselineMetrics,
    CombatPhysicalMetrics CandidateMetrics,
    double ExactFraction,
    double SparseFraction,
    double BudgetExceededFraction);

public sealed record CombatHorizonDeltaReport(
    int Horizon,
    double? PrimaryDeltaEv,
    double ResearchBalancedDeltaEv,
    double ConfidenceLow,
    double ConfidenceHigh,
    double SupportedTargetWeightMass,
    double EffectiveSampleSize,
    IReadOnlyList<CombatCellDeltaReport> Cells);

public sealed record CombatDeckDeltaReport(
    int SchemaVersion,
    int CombatSemanticsVersion,
    string GeneratedAt,
    string PortfolioId,
    string PortfolioHash,
    string HpCalibrationId,
    string HpCalibrationHash,
    string CombatModelHash,
    string Candidate,
    int Ascension,
    int Seed,
    bool RuntimeCandidate,
    string Status,
    IReadOnlyList<CombatHorizonDeltaReport> Horizons,
    IReadOnlyList<string> UnsupportedSamples,
    IReadOnlyList<string> Warnings);

public sealed record CombatPairSampleResult(
    CombatSample Sample,
    int Horizon,
    CombatContextResult Baseline,
    CombatContextResult Candidate,
    double Delta);
