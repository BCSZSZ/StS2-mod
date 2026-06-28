using System.Text.Json.Serialization;

namespace CardValueOverlay.Core.Configuration;

public sealed record OverlaySettings
{
    private const string DefaultDisplayMode = "trainingValue";
    private const string DefaultValueHorizon = "midline";

    [JsonPropertyName("displayMode")]
    public string DisplayModeKey { get; init; } = DefaultDisplayMode;

    [JsonIgnore]
    public OverlayDisplayMode DisplayMode => TryParseDisplayMode(DisplayModeKey, out OverlayDisplayMode mode)
        ? mode
        : OverlayDisplayMode.TrainingValue;

    public string FixedText { get; init; } = "CVO";

    public string FixedTextLocTable { get; init; } = "gameplay_ui";

    public string FixedTextLocKey { get; init; } = "CardValueOverlay.overlay.fixedText";

    [JsonPropertyName("valueHorizon")]
    public string ValueHorizonKey { get; init; } = DefaultValueHorizon;

    [JsonIgnore]
    public TrainingValueHorizon ValueHorizon => TryParseValueHorizon(ValueHorizonKey, out TrainingValueHorizon horizon)
        ? horizon
        : TrainingValueHorizon.Midline;

    public int MaxLines { get; init; } = 3;

    public static bool TryParseDisplayMode(string? value, out OverlayDisplayMode mode)
    {
        switch (value?.Trim())
        {
            case "fixedText":
                mode = OverlayDisplayMode.FixedText;
                return true;
            case "cardName":
                mode = OverlayDisplayMode.CardName;
                return true;
            case "trainingValue":
                mode = OverlayDisplayMode.TrainingValue;
                return true;
            default:
                mode = OverlayDisplayMode.TrainingValue;
                return false;
        }
    }

    public static bool TryParseValueHorizon(string? value, out TrainingValueHorizon horizon)
    {
        switch (value?.Trim())
        {
            case "shortline":
                horizon = TrainingValueHorizon.Shortline;
                return true;
            case "midline":
                horizon = TrainingValueHorizon.Midline;
                return true;
            case "longline":
                horizon = TrainingValueHorizon.Longline;
                return true;
            default:
                horizon = TrainingValueHorizon.Midline;
                return false;
        }
    }
}
