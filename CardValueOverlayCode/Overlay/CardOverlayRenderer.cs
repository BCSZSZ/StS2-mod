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
    // Resolved lazily (not in a static field initializer) per CLAUDE.md's Static Initialization Rule.
    private static System.Reflection.MethodInfo? getTitleTextMethod;
    private static bool getTitleTextResolved;

    private static System.Reflection.MethodInfo? GetTitleTextMethod()
    {
        if (!getTitleTextResolved)
        {
            getTitleTextResolved = true;
            getTitleTextMethod = typeof(NCard).GetMethod(
                "GetTitleText",
                System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.NonPublic);
        }

        return getTitleTextMethod;
    }

    // --- refresh settle tracking (main thread only) ---
    // A refresh poll wraps its render pass in Begin/EndSettleTracking. Every realtime cell rendered
    // calls NoteRealtimeResult; End returns true only when at least one realtime cell was shown and
    // all of them are settled (computed or failed). The poller stops re-rendering once End is true,
    // so it keeps polling through the "deck not readable yet" / "still computing" windows and never
    // stops early. Safe as plain statics because all rendering runs synchronously on the main thread.
    private const int ProgressStagesPerCard = 3;
    private static readonly HashSet<RealtimeEvService.CardEvResult> passSeen = new();
    // Cards already rendered this tracked pass — the reward screen walks holders AND descendants, so
    // the same card is visited twice; skip the 2nd (only while a poll pass is active).
    private static readonly HashSet<NCard> passRenderedCards = new();
    private static bool settleTrackingActive;
    private static int passRealtimeTotal;
    private static int passRealtimeSettled;
    private static int passStageSum;

    public static void BeginSettleTracking()
    {
        passSeen.Clear();
        passRenderedCards.Clear();
        settleTrackingActive = true;
        passRealtimeTotal = 0;
        passRealtimeSettled = 0;
        passStageSum = 0;
    }

    // True once every distinct live cell shown this pass is settled (computed or failed).
    public static bool EndSettleTracking()
    {
        settleTrackingActive = false;
        return passRealtimeTotal > 0 && passRealtimeSettled == passRealtimeTotal;
    }

    // Fine-grained fraction 0..1 of the last render pass's live work (counts per-card sub-stages),
    // so the progress bar can show a smoothly-advancing percentage even with only a few cards.
    public static double PassProgressFraction =>
        passRealtimeTotal <= 0 ? 0d : (double)passStageSum / (passRealtimeTotal * ProgressStagesPerCard);

    // True while some live cell shown this pass is still pending (drives whether to show the bar).
    public static bool PassHasPending => passRealtimeTotal > 0 && passRealtimeSettled < passRealtimeTotal;

    private static void NoteRealtimeResult(RealtimeEvService.CardEvResult result)
    {
        // Dedupe: a card can be rendered twice per pass (holder + descendant walk); count each
        // distinct result once so the progress reads 2/3 not 4/6.
        if (!passSeen.Add(result))
        {
            return;
        }

        passRealtimeTotal++;
        int stage = result.Failed ? ProgressStagesPerCard : Math.Clamp(result.ProgressStage, 0, ProgressStagesPerCard);
        passStageSum += stage;
        if (result.IsSettled)
        {
            passRealtimeSettled++;
        }
    }

    // --- universal overlay refresh pump (main thread only) ---
    // Root fix for the "screen stays on ..." bug. The overlay renders on GAME-driven events
    // (NCard.UpdateVisuals, screen SetCard hooks), but the EV result lands asynchronously seconds
    // later. Screens with a dedicated poll scheduler (reward, upgrade preview) re-render themselves
    // until settled; screens WITHOUT one — the inspect / deck-detail card — rendered once and got
    // stuck. This pump closes the gap for good: any card rendered OUTSIDE a scheduler pass whose live
    // result is not yet settled is re-rendered every tick until it settles (or leaves the tree). One
    // SceneTreeTimer drives every such card, so no new per-screen hook is ever needed again.
    private sealed record PumpEntry(Node? ContextRoot, CardUpgradeState? ForcedState, int Ticks);
    private static readonly Dictionary<NCard, PumpEntry> pumpCards = new();
    private static bool pumpRunning;
    private static SceneTree? pumpTree;
    private static bool renderingFromPump;
    private const double PumpIntervalSeconds = 0.25;
    private const int PumpMaxTicksPerCard = 240; // safety: stop chasing one card after ~60s

    // Set by ResolveTrainingValue each render so Render (via UpdatePumpAfterRender) knows whether an
    // async result backs this card and, if so, whether it has settled.
    private static bool lastRenderRequestedLive;
    private static RealtimeEvService.CardEvResult? lastResolvedLiveResult;

    // Called at the end of a successful (label-shown) render. Registers the card with the pump when it
    // still needs an async result, unregisters it once settled. Scheduler-driven screens re-render
    // themselves, so their in-pass renders are skipped here to avoid redundant double-pumping.
    private static void UpdatePumpAfterRender(NCard cardNode, Node? contextRoot, CardUpgradeState? forcedState)
    {
        if (settleTrackingActive)
        {
            return;
        }

        bool needsPump = lastRenderRequestedLive
            && (lastResolvedLiveResult is null || !lastResolvedLiveResult.IsSettled);
        if (needsPump)
        {
            RegisterPump(cardNode, contextRoot, forcedState);
        }
        else
        {
            UnregisterPump(cardNode);
        }
    }

    private static void RegisterPump(NCard card, Node? contextRoot, CardUpgradeState? forcedState)
    {
        // A game-driven render (re)starts the card's tick count at 0; a pump-driven re-render advances
        // it so the safety cap can eventually fire even if the result never settles.
        int ticks = 0;
        if (renderingFromPump && pumpCards.TryGetValue(card, out PumpEntry? existing))
        {
            ticks = existing.Ticks + 1;
        }

        pumpCards[card] = new PumpEntry(contextRoot, forcedState, ticks);
        try
        {
            pumpTree ??= card.GetTree();
        }
        catch
        {
            // GetTree can throw for a node not yet in the tree; EnsurePump tolerates a null tree.
        }

        EnsurePump();
    }

    private static void UnregisterPump(NCard card)
    {
        pumpCards.Remove(card);
    }

    private static void EnsurePump()
    {
        if (pumpRunning || pumpTree is null || pumpCards.Count == 0)
        {
            return;
        }

        pumpRunning = true;
        ArmPump();
    }

    private static void ArmPump()
    {
        try
        {
            if (pumpTree is null)
            {
                pumpRunning = false;
                return;
            }

            SceneTreeTimer timer = pumpTree.CreateTimer(PumpIntervalSeconds);
            timer.Timeout += PumpTick;
        }
        catch (Exception ex)
        {
            pumpRunning = false;
            MainFile.Logger.Warn($"Overlay refresh pump failed to arm: {ex.Message}", 0);
        }
    }

    private static void PumpTick()
    {
        try
        {
            foreach (NCard card in pumpCards.Keys.ToList())
            {
                if (!GodotObject.IsInstanceValid(card) || !card.IsInsideTree())
                {
                    pumpCards.Remove(card);
                    continue;
                }

                if (!pumpCards.TryGetValue(card, out PumpEntry? entry))
                {
                    continue;
                }

                if (entry.Ticks >= PumpMaxTicksPerCard)
                {
                    pumpCards.Remove(card);
                    continue;
                }

                pumpTree = card.GetTree() ?? pumpTree;

                // Re-render off the game-event path. Render re-reads the (now maybe filled) live result
                // and, via UpdatePumpAfterRender, drops the card from the pump once it settles.
                renderingFromPump = true;
                try
                {
                    Render(card, entry.ContextRoot, entry.ForcedState);
                }
                finally
                {
                    renderingFromPump = false;
                }
            }
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"Overlay refresh pump tick failed: {ex.Message}", 0);
        }
        finally
        {
            if (pumpCards.Count == 0)
            {
                pumpRunning = false;
            }
            else
            {
                ArmPump();
            }
        }
    }

    public static void Render(NCard cardNode, Node? contextRoot = null, CardUpgradeState? forcedUpgradeState = null)
    {
        // Within one settle-tracked poll pass, skip the 2nd render of the same card (reward screen
        // walks holders AND descendants). Only active during a poll pass; normal renders unaffected.
        if (settleTrackingActive && !passRenderedCards.Add(cardNode))
        {
            return;
        }

        RichTextLabel? existing = GetExistingLabel(cardNode);

        // Cheap gate FIRST. NCard.UpdateVisuals fires Render for every combat hand card (contextRoot
        // == null, not shown); compute shouldShow before the expensive ResolveOverlayText (which reads
        // the deck snapshot and queues a background EV) so hidden cards cost nothing.
        bool shouldShow = contextRoot is not null || CardOverlayContext.ShouldShowFor(cardNode);
        if (!shouldShow)
        {
            if (existing is not null)
            {
                existing.Visible = false;
            }

            UnregisterPump(cardNode);
            return;
        }

        CardValueConfig config = RuntimeConfigProvider.Current;
        bool deckView = contextRoot is NInspectCardScreen || CardOverlayContext.IsInspectContext(cardNode);
        bool upgradePreview = !deckView && contextRoot is NUpgradePreview;

        lastRenderRequestedLive = false;
        lastResolvedLiveResult = null;
        string? text = ResolveOverlayText(config, cardNode, forcedUpgradeState, deckView, upgradePreview);
        if (string.IsNullOrWhiteSpace(text))
        {
            if (existing is not null)
            {
                existing.Visible = false;
            }

            UnregisterPump(cardNode);
            return;
        }

        int fontSize = upgradePreview ? UpgradeFontSize : FontSize;
        float width = deckView ? WideWidth : upgradePreview ? UpgradeWidth : StackedWidth;
        float downOffset = deckView ? WideDownOffset : upgradePreview ? UpgradeDownOffset : StackedDownOffset;

        RichTextLabel label = existing ?? CreateLabel(cardNode);
        label.Text = text;
        label.AddThemeFontSizeOverride("normal_font_size", fontSize);
        SetContentSize(label, text, width, fontSize);
        label.Visible = true;
        PositionAboveCard(cardNode, label, downOffset);
        UpdatePumpAfterRender(cardNode, contextRoot, forcedUpgradeState);
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

            // Upgrade preview: the deck holds the unupgraded card, so both forms are valued against a
            // baseline with that unupgraded copy removed (removeUpgrade: 0) — matching ResolveTrainingValue.
            RealtimeEvService.CardEvResult? unupResult = RealtimeEvService.RequestCardEv(cardKey, 0, removeUpgrade: 0);
            RealtimeEvService.CardEvResult? upResult = RealtimeEvService.RequestCardEv(cardKey, 1, removeUpgrade: 0);
            if (unupResult is not null) NoteRealtimeResult(unupResult);
            if (upResult is not null) NoteRealtimeResult(upResult);

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
            return existing; // already configured on creation; re-configuring each poll churned resources
        }

        RichTextLabel label = new() { Name = DeltaLabelName };
        Configure(label);
        previewRoot.AddChild(label);
        return label;
    }

    private const string ProgressLabelName = "CardValueOverlay_ProgressBar";
    private const int ProgressBarSegments = 12;
    private const int ProgressFontSize = 15;
    // Vertical position as a fraction of viewport height. Reward and upgrade screens differ: the
    // reward banner sits lower so its bar goes just above it; the upgrade preview has no banner so
    // its bar goes near the very top.
    public const float ProgressTopFractionReward = 0.24f;
    public const float ProgressTopFractionUpgrade = 0.03f;

    // A single "calculating ▓▓▓░░ 42%" bar centered horizontally at topFraction of the screen height.
    // Shown by the pollers while results are still computing; hides once all are settled. Filled part
    // green, remainder gray. fraction is 0..1 (fine-grained via per-card sub-stages).
    public static void RenderProgressBar(Node screen, double fraction, bool hasPending, float topFraction)
    {
        try
        {
            RichTextLabel? existing = screen.GetNodeOrNull<RichTextLabel>(ProgressLabelName);
            if (!hasPending)
            {
                if (existing is not null)
                {
                    existing.Visible = false;
                }

                return;
            }

            RichTextLabel label = existing ?? CreateProgressLabel(screen);
            int pct = Math.Clamp((int)Math.Round(fraction * 100), 0, 100);
            int filled = Math.Clamp((int)Math.Round(fraction * ProgressBarSegments), 0, ProgressBarSegments);
            string plain = $"calculating {new string('▓', filled)}{new string('░', ProgressBarSegments - filled)} {pct}%";
            string bbcode =
                $"calculating [color={GreenColor}]{new string('▓', filled)}[/color]"
                + $"[color={GrayColor}]{new string('░', ProgressBarSegments - filled)}[/color] {pct}%";

            label.AddThemeFontSizeOverride("normal_font_size", ProgressFontSize);
            label.Text = bbcode;

            float width = GetMonospaceFont().GetStringSize(plain, HorizontalAlignment.Left, -1, ProgressFontSize).X + 16f;
            SetContentSize(label, plain, width, ProgressFontSize);

            Rect2 viewport = screen.GetViewport()?.GetVisibleRect() ?? new Rect2(0f, 0f, width, 600f);
            label.Position = new Vector2((viewport.Size.X - width) / 2f, viewport.Size.Y * topFraction);
            label.Visible = true;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"Failed to render progress bar: {ex.Message}", 0);
        }
    }

    private static RichTextLabel CreateProgressLabel(Node screen)
    {
        RichTextLabel label = new() { Name = ProgressLabelName };
        Configure(label);
        screen.AddChild(label);
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

    private static string? ResolveOverlayText(CardValueConfig config, NCard cardNode, CardUpgradeState? forcedUpgradeState, bool deckView, bool upgradePreview)
    {
        OverlaySettings settings = config.Overlay;
        return settings.DisplayMode switch
        {
            OverlayDisplayMode.FixedText => ResolveFixedText(settings),
            OverlayDisplayMode.CardName => ResolveCardName(cardNode),
            OverlayDisplayMode.TrainingValue => ResolveTrainingValue(config, cardNode, forcedUpgradeState, deckView, upgradePreview),
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

    private static string? ResolveTrainingValue(CardValueConfig config, NCard cardNode, CardUpgradeState? forcedUpgradeState, bool deckView, bool upgradePreview)
    {
        if (cardNode.Model is null)
        {
            return null;
        }

        // This card's overlay depends on an async EV result, so the pump must keep re-rendering it
        // until that result settles (see UpdatePumpAfterRender). Set even when the result is null
        // (deck momentarily unreadable) so the pump retries.
        lastRenderRequestedLive = true;

        bool wide = deckView;
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

        // Kick off (or read) the live, deck-contextual EV. The口径 depends on the screen:
        //  - reward (neither deckView nor upgradePreview): the card is NOT in the deck -> ADD口径
        //    (removeUpgrade = null): baseline = current deck, value = adding this card.
        //  - deck view: the card IS in the deck -> remove that same form (removeUpgrade = probeUpgrade).
        //  - upgrade preview: the deck holds the UNUPGRADED card -> always remove the unupgraded one
        //    (removeUpgrade = 0), so "after" (probeUpgrade=1) values swapping it to the upgraded form.
        int probeUpgrade = upgradeState == CardUpgradeState.Upgraded ? 1 : 0;
        int? removeUpgrade = upgradePreview ? 0 : (deckView ? probeUpgrade : (int?)null);
        RealtimeEvService.CardEvResult? calculated = RealtimeEvService.RequestCardEv(cardKey, probeUpgrade, removeUpgrade);
        lastResolvedLiveResult = calculated;

        // Deck view = you inspected one card: also warm the OTHER upgrade form (value if it were the
        // other form), so only this card's two forms compute — not the whole deck.
        if (deckView)
        {
            RealtimeEvService.RequestCardEv(cardKey, probeUpgrade == 1 ? 0 : 1, removeUpgrade);
        }

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

        NoteRealtimeResult(calculated);
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
        System.Reflection.MethodInfo? method = GetTitleTextMethod();
        if (method is null)
        {
            return null;
        }

        try
        {
            return method.Invoke(cardNode, null) as string;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"Failed to read existing card title text: {ex.Message}", 0);
            return null;
        }
    }

    private static RichTextLabel? GetExistingLabel(NCard cardNode)
    {
        // Configured once at creation (CreateLabel); re-Configuring every render re-set all theme
        // overrides needlessly and churned resources (mirrors GetOrCreateDeltaLabel's fix).
        return cardNode.GetNodeOrNull<RichTextLabel>(LabelName);
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
        label.AddThemeStyleboxOverride("normal", GetBackgroundStyleBox());
    }

    // Shared, cached background panel. MUST be a rooted static (not `new` per Configure call): the
    // settle poll re-renders many times/sec, and creating a fresh StyleBoxFlat each time churned
    // Godot resources so fast the C# GC handle was collected while native still referenced it,
    // throwing "Handle is not initialized" (seen in godot.log from the upgrade-preview poll).
    private static StyleBoxFlat? backgroundStyleBox;

    private static StyleBoxFlat GetBackgroundStyleBox()
    {
        return backgroundStyleBox ??= new StyleBoxFlat
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
