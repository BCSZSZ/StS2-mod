namespace CardValueOverlay.Modeling.Simulation;

/// <summary>
/// Explicit finite-horizon position for one simulated turn. The current turn is included in
/// <see cref="RemainingTurns"/>; <see cref="FutureTurns"/> counts only turns after the current one.
/// Keeping both numbers explicit prevents a 4/8/12-turn solve from accidentally pricing a delayed
/// effect as though the full original horizon were still available on every turn.
/// </summary>
public readonly record struct FiniteHorizonContext(int HorizonTurns, int CurrentTurn)
{
    public int RemainingTurns => Math.Max(0, HorizonTurns - CurrentTurn + 1);

    public int FutureTurns => Math.Max(0, HorizonTurns - CurrentTurn);

    public bool HasFutureTurn => FutureTurns > 0;
}
