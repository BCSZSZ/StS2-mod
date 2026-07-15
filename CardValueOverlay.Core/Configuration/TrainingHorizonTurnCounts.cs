namespace CardValueOverlay.Core.Configuration;

public static class TrainingHorizonTurnCounts
{
    public const int Shortline = 4;
    public const int Midline = 8;
    public const int Longline = 12;

    public static int Resolve(TrainingValueHorizon horizon)
    {
        return horizon switch
        {
            TrainingValueHorizon.Shortline => Shortline,
            TrainingValueHorizon.Midline => Midline,
            TrainingValueHorizon.Longline => Longline,
            _ => throw new ArgumentOutOfRangeException(nameof(horizon), horizon, null)
        };
    }
}
