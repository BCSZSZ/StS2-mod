using CardValueOverlay.Core.Configuration;
using CardValueOverlay.Core.Values;
using CardValueOverlay.CardValueOverlayCode.Runtime;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using System.Globalization;

namespace CardValueOverlay.CardValueOverlayCode.Overlay;

public static class CardOverlayRenderer
{
    private const string LabelName = "CardValueOverlay_PrimaryLabel";
    private static readonly Vector2 LabelSize = new(240, 58);
    private static CardValueConfig? cachedConfig;
    private static ValueResolver? cachedResolver;
    private static readonly System.Reflection.MethodInfo? GetTitleTextMethod =
        typeof(NCard).GetMethod(
            "GetTitleText",
            System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.NonPublic);

    public static void Render(NCard cardNode, Node? contextRoot = null, CardUpgradeState? forcedUpgradeState = null)
    {
        CardValueConfig config = RuntimeConfigProvider.Current;
        OverlaySettings settings = config.Overlay;
        string? text = ResolveOverlayText(config, cardNode, forcedUpgradeState);
        bool shouldShow = contextRoot is not null || CardOverlayContext.ShouldShowFor(cardNode);
        Label? existing = GetExistingLabel(cardNode);

        if (!shouldShow || string.IsNullOrWhiteSpace(text))
        {
            if (existing is not null)
            {
                existing.Visible = false;
            }

            return;
        }

        Label label = existing ?? CreateLabel(cardNode);
        label.Text = text;
        label.Visible = true;
        PositionAboveCard(cardNode, label);
    }

    public static void RenderInspectScreen(NInspectCardScreen screen, NCard? card)
    {
        if (card is not null)
        {
            Render(card, screen);
            return;
        }

        RenderDescendantCards(screen, screen);
    }

    public static void RenderRewardScreen(NCardRewardSelectionScreen screen)
    {
        RenderDescendantCardHolders(screen, screen);
        RenderDescendantCards(screen, screen);
    }

    public static void RenderDescendantCards(Node root, Node? contextRoot = null)
    {
        foreach (Node child in root.GetChildren())
        {
            if (child is NCard card)
            {
                Render(card, contextRoot);
            }

            RenderDescendantCards(child, contextRoot);
        }
    }

    // Render every NCard under root with an explicitly forced upgrade state. Used by the upgrade
    // preview, where the "before" card must always show unupgraded values and the "after" preview
    // card must always show upgraded values regardless of how the preview clone reports its level.
    public static void RenderCardsWithForcedState(Node root, Node contextRoot, CardUpgradeState forcedUpgradeState)
    {
        foreach (Node child in root.GetChildren())
        {
            if (child is NCard card)
            {
                Render(card, contextRoot, forcedUpgradeState);
            }

            RenderCardsWithForcedState(child, contextRoot, forcedUpgradeState);
        }
    }

    public static void RenderDescendantCardHolders(Node root, Node? contextRoot = null)
    {
        foreach (Node child in root.GetChildren())
        {
            if (child is NCardHolder holder)
            {
                RenderHolder(holder, contextRoot);
            }

            RenderDescendantCardHolders(child, contextRoot);
        }
    }

    public static void RenderHolder(NCardHolder holder, Node? contextRoot = null)
    {
        Node? resolvedContext = contextRoot;
        if (resolvedContext is null && !CardOverlayContext.TryGetDisplayContext(holder, out resolvedContext))
        {
            return;
        }

        if (holder.CardNode is NCard card)
        {
            Render(card, resolvedContext);
        }
    }

    private static string? ResolveOverlayText(CardValueConfig config, NCard cardNode, CardUpgradeState? forcedUpgradeState = null)
    {
        OverlaySettings settings = config.Overlay;
        return settings.DisplayMode switch
        {
            OverlayDisplayMode.FixedText => ResolveFixedText(settings),
            OverlayDisplayMode.CardName => ResolveCardName(cardNode),
            OverlayDisplayMode.TrainingValue => ResolveTrainingValue(config, cardNode, forcedUpgradeState),
            _ => null
        };
    }

    private static string? ResolveFixedText(OverlaySettings settings)
    {
        return LocalizedText.Resolve(
            settings.FixedTextLocTable,
            settings.FixedTextLocKey,
            settings.FixedText);
    }

    private static string? ResolveCardName(NCard cardNode)
    {
        string? titleText = TryGetExistingTitleText(cardNode);
        if (!string.IsNullOrWhiteSpace(titleText))
        {
            return titleText;
        }

        try
        {
            string? modelTitle = cardNode.Model?.Title;
            return string.IsNullOrWhiteSpace(modelTitle) ? null : modelTitle;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"Failed to resolve card title: {ex.Message}", 0);
            return null;
        }
    }

    private static string? ResolveTrainingValue(CardValueConfig config, NCard cardNode, CardUpgradeState? forcedUpgradeState = null)
    {
        if (cardNode.Model is null)
        {
            return null;
        }

        string cardKey = cardNode.Model.Id.ToString();
        CardUpgradeState upgradeState = forcedUpgradeState
            ?? (cardNode.Model.CurrentUpgradeLevel > 0
                ? CardUpgradeState.Upgraded
                : CardUpgradeState.Unupgraded);
        ValueResolver resolver = GetResolver(config);
        EffectiveValue<double> shortline = resolver.ResolveCardValue(cardKey, upgradeState, TrainingValueHorizon.Shortline);
        EffectiveValue<double> midline = resolver.ResolveCardValue(cardKey, upgradeState, TrainingValueHorizon.Midline);
        EffectiveValue<double> longline = resolver.ResolveCardValue(cardKey, upgradeState, TrainingValueHorizon.Longline);
        if (shortline.Value is null && midline.Value is null && longline.Value is null)
        {
            return null;
        }

        int maxLines = Math.Clamp(config.Overlay.MaxLines, 1, 3);
        if (maxLines == 1)
        {
            TrainingValueHorizon horizon = config.Overlay.ValueHorizon;
            EffectiveValue<double> value = resolver.ResolveCardValue(cardKey, upgradeState, horizon);
            return $"{HorizonLabel(horizon)}: {FormatTrainingValue(value.Value)}";
        }

        List<string> lines =
        [
            $"short: {FormatTrainingValue(shortline.Value)}",
            $"mid: {FormatTrainingValue(midline.Value)}",
            $"long: {FormatTrainingValue(longline.Value)}"
        ];
        return string.Join('\n', lines.Take(maxLines));
    }

    private static ValueResolver GetResolver(CardValueConfig config)
    {
        if (!ReferenceEquals(cachedConfig, config) || cachedResolver is null)
        {
            cachedConfig = config;
            cachedResolver = new ValueResolver(config);
        }

        return cachedResolver;
    }

    private static string HorizonLabel(TrainingValueHorizon horizon)
    {
        return horizon switch
        {
            TrainingValueHorizon.Shortline => "short",
            TrainingValueHorizon.Midline => "mid",
            TrainingValueHorizon.Longline => "long",
            _ => "EV"
        };
    }

    private static string FormatTrainingValue(double? value)
    {
        return value is double resolved
            ? resolved.ToString("0.#", CultureInfo.InvariantCulture)
            : "--";
    }

    private static string? TryGetExistingTitleText(NCard cardNode)
    {
        if (GetTitleTextMethod is null)
        {
            return null;
        }

        try
        {
            return GetTitleTextMethod.Invoke(cardNode, null) as string;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"Failed to read existing card title text: {ex.Message}", 0);
            return null;
        }
    }

    private static Label? GetExistingLabel(NCard cardNode)
    {
        Label? existing = cardNode.GetNodeOrNull<Label>(LabelName);
        if (existing is not null)
        {
            Configure(existing);
        }

        return existing;
    }

    private static Label CreateLabel(NCard cardNode)
    {
        Label label = new()
        {
            Name = LabelName
        };
        Configure(label);
        cardNode.AddChild(label);
        return label;
    }

    private static void Configure(Label label)
    {
        label.MouseFilter = Control.MouseFilterEnum.Ignore;
        label.TopLevel = true;
        label.ZIndex = 4096;
        label.Size = LabelSize;
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.VerticalAlignment = VerticalAlignment.Center;
        label.AutowrapMode = TextServer.AutowrapMode.Off;
        label.ClipText = true;

        label.AddThemeFontSizeOverride("font_size", 13);
        label.AddThemeColorOverride("font_color", Colors.White);
        label.AddThemeColorOverride("font_outline_color", Colors.Black);
        label.AddThemeConstantOverride("outline_size", 4);
    }

    public static void PositionAboveCard(NCard cardNode, Label label)
    {
        if (!cardNode.IsInsideTree())
        {
            label.Visible = false;
            return;
        }

        Transform2D transform = cardNode.GetGlobalTransformWithCanvas();
        Vector2 scale = transform.Scale;
        Vector2 currentSize = cardNode.GetCurrentSize();

        float scaleX = MathF.Max(MathF.Abs(scale.X), 0.001f);
        float scaleY = MathF.Max(MathF.Abs(scale.Y), 0.001f);
        Vector2 localSize = new(currentSize.X / scaleX, currentSize.Y / scaleY);

        if (localSize.X < 1f || localSize.Y < 1f)
        {
            localSize = new Vector2(520f, 720f);
        }

        float localGap = 18f / scaleY;
        Vector2 localTopCenter = new(0f, -localSize.Y / 2f - localGap);
        Vector2 globalTopCenter = transform * localTopCenter;

        label.GlobalPosition = new Vector2(
            globalTopCenter.X - label.Size.X / 2f,
            globalTopCenter.Y - label.Size.Y);
    }
}
