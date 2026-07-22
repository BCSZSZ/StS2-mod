namespace CardValueOverlay.Modeling.Combat.Portfolio;

public sealed record CombatPortfolioDefinition(
    int SchemaVersion,
    string PortfolioId,
    int Ascension,
    string Status,
    string TargetWeightStatus,
    string DeckSource,
    IReadOnlyList<CombatPortfolioCell> Cells,
    IReadOnlyList<string> Notes);

public sealed record CombatPortfolioCell(
    string Id,
    int Act,
    string Tier,
    string HpContextId,
    double TargetWeight,
    int ProposalSamples,
    IReadOnlyList<string> DeckGroups,
    IReadOnlyList<string> EncounterSelectors,
    int MinimumUniqueEncounters,
    StartHpSourceSpec StartHp);

public sealed record StartHpSourceSpec(
    string Kind,
    int MaxHp,
    IReadOnlyList<int> Values);

public sealed record CombatSample(
    string SampleId,
    string CellId,
    int Act,
    string Tier,
    string DeckRunId,
    string DeckGroup,
    string EncounterId,
    int PlayerHp,
    int PlayerMaxHp,
    string HpContextId,
    ulong RunKey,
    double ProposalProbability,
    double TargetProbability,
    double ImportanceWeight,
    bool Supported,
    string? UnsupportedReason,
    IReadOnlyDictionary<int, int>? MonsterHpByPosition = null,
    IReadOnlyDictionary<int, string>? InitialIntentByPosition = null);

public sealed record CombatSamplePlan(
    string PortfolioId,
    string PortfolioHash,
    int Seed,
    IReadOnlyList<CombatSample> Samples,
    double EffectiveSampleSize,
    IReadOnlyList<string> Warnings);
