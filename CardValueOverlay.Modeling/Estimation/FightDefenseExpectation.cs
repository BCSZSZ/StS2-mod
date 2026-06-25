namespace CardValueOverlay.Modeling.Estimation;

public sealed record FightDefenseExpectation(
    string FightType,
    decimal ExpectedTurns,
    decimal ExpectedDamage,
    decimal AscensionExpectedDamage,
    decimal ExpectedWeak,
    decimal ExpectedVulnerable,
    decimal ExpectedFrail,
    decimal ExpectedStrengthGain);
