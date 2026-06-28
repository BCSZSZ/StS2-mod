namespace CardValueOverlay.Core.Configuration;

public sealed record OverlaySettings
{
    public OverlayDisplayMode DisplayMode { get; init; } = OverlayDisplayMode.TrainingValue;

    public string FixedText { get; init; } = "CVO";

    public string FixedTextLocTable { get; init; } = "gameplay_ui";

    public string FixedTextLocKey { get; init; } = "CardValueOverlay.overlay.fixedText";

    public TrainingValueHorizon ValueHorizon { get; init; } = TrainingValueHorizon.Midline;

    public int MaxLines { get; init; } = 3;
}
