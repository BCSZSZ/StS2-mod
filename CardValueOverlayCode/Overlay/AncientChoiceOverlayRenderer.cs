using CardValueOverlay.CardValueOverlayCode.Runtime;
using CardValueOverlay.Core.Adoption;
using CardValueOverlay.Core.Ancient;
using Godot;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Events;

namespace CardValueOverlay.CardValueOverlayCode.Overlay;

public static class AncientChoiceOverlayRenderer
{
    private const string LabelName = "CardValueOverlay_AncientChoiceLabel";
    private static readonly Color Green = Color.FromHtml("#73f06a");
    private static readonly Color Red = Color.FromHtml("#ff5b5b");
    private static readonly Color Gray = Color.FromHtml("#b8b8b8");

    public static void Render(NEventOptionButton button)
    {
        if (button.Event is not AncientEventModel || button.Option.IsProceed)
        {
            Hide(button);
            return;
        }

        Label label = GetOrCreateLabel(button);
        AncientChoiceStatsPair stats = AncientChoiceStatsProvider.Resolve(button.Option.TextKey);
        label.Text = $"pick G {FormatPickRate(stats.Global)} / L {FormatPickRate(stats.Local)}\n"
            + $"picked winrate G {FormatPickedWinRate(stats.Global)} / L {FormatPickedWinRate(stats.Local)}";
        label.Modulate = ColorFor(stats.Global?.PickRateBand ?? CardAdoptionStatBand.Unknown);
        label.Visible = true;
    }

    public static void Hide(NEventOptionButton button)
    {
        Label? label = button.GetNodeOrNull<Label>(LabelName);
        if (label is not null)
        {
            label.Visible = false;
        }
    }

    private static Label GetOrCreateLabel(NEventOptionButton button)
    {
        Label? existing = button.GetNodeOrNull<Label>(LabelName);
        if (existing is not null)
        {
            return existing;
        }

        Label label = new()
        {
            Name = LabelName,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        label.AddThemeFontSizeOverride("font_size", 18);
        label.AddThemeColorOverride("font_outline_color", Colors.Black);
        label.AddThemeConstantOverride("outline_size", 3);
        label.AnchorLeft = 1f;
        label.AnchorRight = 1f;
        label.AnchorTop = 0f;
        label.AnchorBottom = 0f;
        label.OffsetLeft = -500f;
        label.OffsetRight = -14f;
        label.OffsetTop = 6f;
        label.OffsetBottom = 58f;
        label.ZIndex = 50;
        button.AddChild(label);
        return label;
    }

    private static string FormatPickRate(AncientChoiceDisplayStats? stats)
    {
        return stats?.PickRate is not double pickRate || stats.OfferCount <= 0
            ? "--"
            : $"{(pickRate * 100d).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)}% "
                + $"({stats.PickCount}/{stats.OfferCount})";
    }

    private static string FormatPickedWinRate(AncientChoiceDisplayStats? stats)
    {
        return stats?.PickedWinRate is not double pickedWinRate || stats.PickedRunCount <= 0
            ? "--"
            : $"{(pickedWinRate * 100d).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)}% "
                + $"({stats.PickedWinCount}/{stats.PickedRunCount})";
    }

    private static Color ColorFor(CardAdoptionStatBand band)
    {
        return band switch
        {
            CardAdoptionStatBand.High => Green,
            CardAdoptionStatBand.Low => Red,
            CardAdoptionStatBand.Unknown => Gray,
            _ => Colors.White
        };
    }
}
