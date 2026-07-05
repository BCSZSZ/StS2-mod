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
    // Three contexts, each with its own font/width/vertical offset:
    //   Reward screen  — three cards side by side; stacked tables, current size (good).
    //   Upgrade preview — two large cards; stacked tables, BIGGER font + pushed higher.
    //   Deck/inspect view — one large card; wide (side-by-side) tables.
    // Height is fit to the actual line count so the background hugs the content.
    private const float StackedWidth = 208f;
    private const float WideWidth = 324f;
    private const float UpgradeWidth = 244f;
    private const int FontSize = 13;
    private const int UpgradeFontSize = 15;
    private const float StyleboxVerticalMargin = 6f; // content margins top+bottom (3+3)

    // Screen-space distance the block is pushed down into the card node's top padding, per screen.
    // Reward screen felt right at 40; the large inspect card needs less.
    private const float StackedDownOffset = 40f;
    private const float WideDownOffset = 14f;
    private const float UpgradeDownOffset = -14f; // negative = higher (above the card node top)
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
        bool deckView = contextRoot is NInspectCardScreen || CardOverlayContext.IsInspectContext(cardNode);
        bool upgradePreview = !deckView && contextRoot is NUpgradePreview;
        bool wide = deckView;
        int fontSize = upgradePreview ? UpgradeFontSize : FontSize;
        float width = deckView ? WideWidth : upgradePreview ? UpgradeWidth : StackedWidth;
        float downOffset = deckView ? WideDownOffset : upgradePreview ? UpgradeDownOffset : StackedDownOffset;

        string? text = ResolveOverlayText(config, cardNode, forcedUpgradeState, wide);
        bool shouldShow = contextRoot is not null || CardOverlayContext.ShouldShowFor(cardNode);
        RichTextLabel? existing = GetExistingLabel(cardNode);

        if (!shouldShow || string.IsNullOrWhiteSpace(text))
        {
            if (existing is not null)
            {
                existing.Visible = false;
            }

            return;
        }

        RichTextLabel label = existing ?? CreateLabel(cardNode);
        label.Text = text;
        label.AddThemeFontSizeOverride("normal_font_size", fontSize);
        SetContentSize(label, text, width, fontSize);
        label.Visible = true;
        PositionAboveCard(cardNode, label, downOffset);
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

    private const string DeltaLabelName = "CardValueOverlay_UpgradeDeltaLabel";
    private const float DeltaWidth = 244f;
    private const int StackedTableLineCount = 9; // est/calc/ΔEV header+3 + blank + total/after header+3

    // Upgrade preview: a small table BETWEEN the before/after cards showing the per-value
    // improvement (upgraded - unupgraded) for est / calc / ΔEV, colored by sign.
    public static void RenderUpgradeDelta(Node previewRoot, Node beforeRoot, Node afterRoot)
    {
        try
        {
            NCard? before = FindFirstCard(beforeRoot);
            NCard? after = FindFirstCard(afterRoot);
            if (before?.Model is null || after is null || !before.IsInsideTree() || !after.IsInsideTree())
            {
                return;
            }

            CardValueConfig config = RuntimeConfigProvider.Current;
            if (config.Overlay.DisplayMode != OverlayDisplayMode.TrainingValue)
            {
                return;
            }

            string cardKey = before.Model.Id.ToString();
            ValueResolver resolver = GetResolver(config);

            double? EstDelta(TrainingValueHorizon horizon)
            {
                double? unup = resolver.ResolveCardValue(cardKey, CardUpgradeState.Unupgraded, horizon).Value;
                double? up = resolver.ResolveCardValue(cardKey, CardUpgradeState.Upgraded, horizon).Value;
                return unup is double u && up is double g ? g - u : null;
            }

            RealtimeEvService.CardEvResult? unupResult = RealtimeEvService.RequestCardEv(cardKey, 0);
            RealtimeEvService.CardEvResult? upResult = RealtimeEvService.RequestCardEv(cardKey, 1);

            RichTextLabel label = GetOrCreateDeltaLabel(previewRoot);
            string deltaText = BuildUpgradeDeltaText(EstDelta, unupResult, upResult);
            label.Text = deltaText;
            label.AddThemeFontSizeOverride("normal_font_size", UpgradeFontSize);
            SetContentSize(label, deltaText, DeltaWidth, UpgradeFontSize);
            label.Visible = true;

            // Center the delta on the two overlay TABLES' midline (they sit above the cards), not on
            // the card art. Uses the upgrade-preview layout (bigger font, pushed higher).
            float lineHeight = GetMonospaceFont().GetHeight(UpgradeFontSize);
            float tablesHeight = StackedTableLineCount * lineHeight + StyleboxVerticalMargin;
            Vector2 beforeAnchor = TableAnchor(before, UpgradeDownOffset);
            Vector2 afterAnchor = TableAnchor(after, UpgradeDownOffset);
            float midX = (beforeAnchor.X + afterAnchor.X) * 0.5f;
            float tablesCenterY = beforeAnchor.Y - tablesHeight / 2f;
            label.GlobalPosition = new Vector2(midX - label.Size.X / 2f, tablesCenterY - label.Size.Y / 2f);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"Failed to render upgrade delta: {ex.Message}", 0);
        }
    }

    private static string BuildUpgradeDeltaText(
        Func<TrainingValueHorizon, double?> estDelta,
        RealtimeEvService.CardEvResult? unup,
        RealtimeEvService.CardEvResult? up)
    {
        string Cell(double? value)
        {
            string plain = value is double v ? v.ToString("+0.#;-0.#;0", CultureInfo.InvariantCulture) : "...";
            return ColorBySign(plain.PadRight(CellWidth), value);
        }

        double? Diff(double? unupValue, double? upValue) =>
            unupValue is double u && upValue is double g ? g - u : null;

        string Row(string label, TrainingValueHorizon horizon, double? unupCalc, double? upCalc, double? unupDelta, double? upDelta) =>
            $"{label,-6} {Cell(estDelta(horizon))} {Cell(Diff(unupCalc, upCalc))} {Cell(Diff(unupDelta, upDelta))}";

        return string.Join('\n',
        [
            "Δupg   est    calc   ΔEV",
            Row("short", TrainingValueHorizon.Shortline, unup?.CalcShort, up?.CalcShort, unup?.DeltaShort, up?.DeltaShort),
            Row("mid", TrainingValueHorizon.Midline, unup?.CalcMid, up?.CalcMid, unup?.DeltaMid, up?.DeltaMid),
            Row("long", TrainingValueHorizon.Longline, unup?.CalcLong, up?.CalcLong, unup?.DeltaLong, up?.DeltaLong)
        ]);
    }

    private static NCard? FindFirstCard(Node root)
    {
        if (root is NCard card)
        {
            return card;
        }

        foreach (Node child in root.GetChildren())
        {
            NCard? found = FindFirstCard(child);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private static RichTextLabel GetOrCreateDeltaLabel(Node previewRoot)
    {
        RichTextLabel? existing = previewRoot.GetNodeOrNull<RichTextLabel>(DeltaLabelName);
        if (existing is not null)
        {
            Configure(existing);
            return existing;
        }

        RichTextLabel label = new() { Name = DeltaLabelName };
        Configure(label);
        previewRoot.AddChild(label);
        return label;
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

    private static string? ResolveOverlayText(CardValueConfig config, NCard cardNode, CardUpgradeState? forcedUpgradeState, bool wide)
    {
        OverlaySettings settings = config.Overlay;
        return settings.DisplayMode switch
        {
            OverlayDisplayMode.FixedText => ResolveFixedText(settings),
            OverlayDisplayMode.CardName => ResolveCardName(cardNode),
            OverlayDisplayMode.TrainingValue => ResolveTrainingValue(config, cardNode, forcedUpgradeState, wide),
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

    private static string? ResolveTrainingValue(CardValueConfig config, NCard cardNode, CardUpgradeState? forcedUpgradeState, bool wide)
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
        bool hasEstimate = shortline.Value is not null || midline.Value is not null || longline.Value is not null;

        // Kick off (or read) the live, deck-contextual EV of adding this card. Runs on a
        // background thread; returns null when no run is active (main menu, etc.).
        int probeUpgrade = upgradeState == CardUpgradeState.Upgraded ? 1 : 0;
        RealtimeEvService.CardEvResult? calculated = RealtimeEvService.RequestCardEv(cardKey, probeUpgrade);

        if (!hasEstimate && calculated is null)
        {
            return null;
        }

        if (calculated is null)
        {
            // No live run: fall back to the single estimate column (original behavior), colored by sign.
            int maxLines = Math.Clamp(config.Overlay.MaxLines, 1, 3);
            if (maxLines == 1)
            {
                TrainingValueHorizon horizon = config.Overlay.ValueHorizon;
                EffectiveValue<double> value = resolver.ResolveCardValue(cardKey, upgradeState, horizon);
                return $"{HorizonLabel(horizon)}: {ColorBySign(FormatTrainingValue(value.Value), value.Value)}";
            }

            return string.Join('\n',
            [
                $"short: {ColorBySign(FormatTrainingValue(shortline.Value), shortline.Value)}",
                $"mid: {ColorBySign(FormatTrainingValue(midline.Value), midline.Value)}",
                $"long: {ColorBySign(FormatTrainingValue(longline.Value), longline.Value)}"
            ]);
        }

        return BuildEstimateVsCalculated(shortline.Value, midline.Value, longline.Value, calculated, wide);
    }

    // Two stacked tables (monospace, bottom-anchored so table 1 rises and table 2 sits below it):
    //  Table 1 (per card): estimate | calc (value per direct play) | ΔEV (deck strength change).
    //  Table 2 (whole deck): total (baseline EV) | after (normal EV with the card).
    // Live cells show "..." until the background sim fills them, "n/a" on failure.
    private const int CellWidth = 6;

    private static string BuildEstimateVsCalculated(
        double? shortEstimate,
        double? midEstimate,
        double? longEstimate,
        RealtimeEvService.CardEvResult calculated,
        bool wide)
    {
        bool failed = calculated.Failed;
        static string Pad(string s) => s.PadRight(CellWidth);
        static string Tag(string color, string s) => $"[color={color}]{s}[/color]";
        static string Gray(string s) => Tag(GrayColor, s);

        // est: always white (gray when absent).
        static string EstCell(double? value) =>
            value is double r ? Pad(r.ToString("0.#", CultureInfo.InvariantCulture)) : Gray(Pad("--"));

        // calc: white, but red when negative.
        string CalcCell(double? value)
        {
            if (failed) return Gray(Pad("n/a"));
            if (value is not double r) return Gray(Pad("..."));
            string p = Pad(r.ToString("0.#", CultureInfo.InvariantCulture));
            return r < 0 ? Tag(RedColor, p) : p;
        }

        // ΔEV: green positive, red negative, white zero.
        string DeltaCell(double? value)
        {
            if (failed) return Gray(Pad("n/a"));
            if (value is not double r) return Gray(Pad("..."));
            string p = Pad(r.ToString("+0.#;-0.#;0", CultureInfo.InvariantCulture));
            return r > 0 ? Tag(GreenColor, p) : r < 0 ? Tag(RedColor, p) : p;
        }

        // total (baseline): always white.
        string TotalCell(double? value)
        {
            if (failed) return Gray(Pad("n/a"));
            return value is double r ? Pad(r.ToString("0.#", CultureInfo.InvariantCulture)) : Gray(Pad("..."));
        }

        // after: green if higher than total, red if lower, white if equal.
        string AfterCell(double? after, double? total)
        {
            if (failed) return Gray(Pad("n/a"));
            if (after is not double a) return Gray(Pad("..."));
            string p = Pad(a.ToString("0.#", CultureInfo.InvariantCulture));
            if (total is not double t) return p;
            return a > t ? Tag(GreenColor, p) : a < t ? Tag(RedColor, p) : p;
        }

        // Left table (per card): est | calc | ΔEV. Right table (whole deck): total | after.
        string L1(string row, double? est, double? calc, double? delta) =>
            $"{row,-6} {EstCell(est)} {CalcCell(calc)} {DeltaCell(delta)}";
        string T2(double? baseline, double? after) =>
            $"{TotalCell(baseline)} {AfterCell(after, baseline)}";

        if (wide)
        {
            // Deck/inspect view: the two tables sit side by side (width is available).
            return string.Join('\n',
            [
                "       est    calc   ΔEV      total  after",
                $"{L1("short", shortEstimate, calculated.CalcShort, calculated.DeltaShort)}   {T2(calculated.BaselineShort, calculated.AfterShort)}",
                $"{L1("mid", midEstimate, calculated.CalcMid, calculated.DeltaMid)}   {T2(calculated.BaselineMid, calculated.AfterMid)}",
                $"{L1("long", longEstimate, calculated.CalcLong, calculated.DeltaLong)}   {T2(calculated.BaselineLong, calculated.AfterLong)}"
            ]);
        }

        // Reward screen: the two tables stack vertically (narrow).
        return string.Join('\n',
        [
            "       est    calc   ΔEV",
            L1("short", shortEstimate, calculated.CalcShort, calculated.DeltaShort),
            L1("mid", midEstimate, calculated.CalcMid, calculated.DeltaMid),
            L1("long", longEstimate, calculated.CalcLong, calculated.DeltaLong),
            "",
            "       total  after",
            $"short  {T2(calculated.BaselineShort, calculated.AfterShort)}",
            $"mid    {T2(calculated.BaselineMid, calculated.AfterMid)}",
            $"long   {T2(calculated.BaselineLong, calculated.AfterLong)}"
        ]);
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

    private static RichTextLabel? GetExistingLabel(NCard cardNode)
    {
        RichTextLabel? existing = cardNode.GetNodeOrNull<RichTextLabel>(LabelName);
        if (existing is not null)
        {
            Configure(existing);
        }

        return existing;
    }

    private static RichTextLabel CreateLabel(NCard cardNode)
    {
        RichTextLabel label = new()
        {
            Name = LabelName
        };
        Configure(label);
        cardNode.AddChild(label);
        return label;
    }

    // A monospace system font so the est/calc/ΔEV columns line up (a proportional font makes
    // space-padded columns drift). SystemFont pulls an OS font, so nothing needs bundling.
    private static Font? monospaceFont;

    private static Font GetMonospaceFont()
    {
        return monospaceFont ??= new SystemFont
        {
            FontNames = ["Consolas", "Courier New", "Cascadia Mono", "monospace"]
        };
    }

    // RichTextLabel (not Label) so we can color individual values green/red via BBCode while the
    // monospace font keeps columns aligned (color tags are zero-width).
    private static void Configure(RichTextLabel label)
    {
        label.MouseFilter = Control.MouseFilterEnum.Ignore;
        label.TopLevel = true;
        label.ZIndex = 4096;
        label.BbcodeEnabled = true;
        label.ScrollActive = false;
        label.AutowrapMode = TextServer.AutowrapMode.Off;
        label.ClipContents = true;

        label.AddThemeFontOverride("normal_font", GetMonospaceFont());
        label.AddThemeFontSizeOverride("normal_font_size", FontSize);
        label.AddThemeConstantOverride("line_separation", 0);
        label.AddThemeColorOverride("default_color", Colors.White);
        label.AddThemeColorOverride("font_outline_color", Colors.Black);
        label.AddThemeConstantOverride("outline_size", 4);

        // Small black semi-transparent panel behind the numbers for readability over card art.
        StyleBoxFlat background = new()
        {
            BgColor = new Color(0f, 0f, 0f, 0.5f),
            ContentMarginLeft = 5f,
            ContentMarginRight = 5f,
            ContentMarginTop = 3f,
            ContentMarginBottom = 3f,
            CornerRadiusTopLeft = 3,
            CornerRadiusTopRight = 3,
            CornerRadiusBottomLeft = 3,
            CornerRadiusBottomRight = 3
        };
        label.AddThemeStyleboxOverride("normal", background);
    }

    public static void PositionAboveCard(NCard cardNode, RichTextLabel label, float downOffset = StackedDownOffset)
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

        // Push the block DOWN into the card node's transparent top padding so it sits closer to the
        // visible art (the node bounding box extends well above the art). Larger downOffset = lower.
        float localGap = -downOffset / scaleY;
        Vector2 localTopCenter = new(0f, -localSize.Y / 2f - localGap);
        Vector2 globalTopCenter = transform * localTopCenter;

        label.GlobalPosition = new Vector2(
            globalTopCenter.X - label.Size.X / 2f,
            globalTopCenter.Y - label.Size.Y);
    }

    // The canvas-space anchor (card top-center) PositionAboveCard uses as a table's bottom edge.
    // Lets the upgrade-delta table align with the two overlay tables instead of the card art.
    private static Vector2 TableAnchor(NCard card, float downOffset)
    {
        Transform2D transform = card.GetGlobalTransformWithCanvas();
        Vector2 scale = transform.Scale;
        float scaleX = MathF.Max(MathF.Abs(scale.X), 0.001f);
        float scaleY = MathF.Max(MathF.Abs(scale.Y), 0.001f);
        Vector2 currentSize = card.GetCurrentSize();
        Vector2 localSize = new(currentSize.X / scaleX, currentSize.Y / scaleY);
        if (localSize.X < 1f || localSize.Y < 1f)
        {
            localSize = new Vector2(520f, 720f);
        }

        float localGap = -downOffset / scaleY;
        Vector2 localTopCenter = new(0f, -localSize.Y / 2f - localGap);
        return transform * localTopCenter;
    }

    // Color a value cell by sign: positive green, negative red, zero/unknown gray. The visible text
    // is padded to the column width BEFORE wrapping so monospace columns stay aligned.
    internal const string GreenColor = "#5fd35f";
    internal const string RedColor = "#ef5f5f";
    internal const string GrayColor = "#b9bfc6";

    internal static string ColorBySign(string paddedText, double? value)
    {
        string color = value is not double v
            ? GrayColor
            : v > 0 ? GreenColor : v < 0 ? RedColor : GrayColor;
        return $"[color={color}]{paddedText}[/color]";
    }

    // Fit the label height to its line count so the semi-transparent background hugs the content
    // (a fixed height left empty rows of background below the last row).
    private static void SetContentSize(RichTextLabel label, string text, float width, int fontSize)
    {
        int lines = 1;
        foreach (char c in text)
        {
            if (c == '\n')
            {
                lines++;
            }
        }

        float lineHeight = GetMonospaceFont().GetHeight(fontSize);
        label.Size = new Vector2(width, lines * lineHeight + StyleboxVerticalMargin);
    }
}
