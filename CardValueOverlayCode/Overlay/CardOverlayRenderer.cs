using CardValueOverlay.Core.Adoption;
using CardValueOverlay.Core.Configuration;
using CardValueOverlay.CardValueOverlayCode.Configuration;
using CardValueOverlay.CardValueOverlayCode.Runtime;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using System.Globalization;

namespace CardValueOverlay.CardValueOverlayCode.Overlay;

public static class CardOverlayRenderer
{
    private const string LabelName = "CardValueOverlay_PrimaryLabel";
    // Three contexts, each with its own font/width/vertical offset:
    //   Reward screen  - three cards side by side; compact dEV and choice tables.
    //   Upgrade preview - two large cards; BIGGER font + pushed higher.
    //   Deck/inspect view - one large card; wider table block.
    // Height is fit to the actual line count so the background hugs the content.
    private const float StackedWidth = 300f;
    private const float WideWidth = 330f;
    private const float UpgradeWidth = 370f;
    private const float ShopWidth = 260f;
    private const int FontSize = 13;
    private const int UpgradeFontSize = 15;
    private const int ShopFontSize = 11;
    private const float StyleboxVerticalMargin = 6f; // content margins top+bottom (3+3)
    private const int MeanColumnWidth = 6;
    private const int IntervalColumnWidth = 17;
    private const int RunsColumnWidth = 5;
    private const int ChoiceLabelColumnWidth = 7;
    private const int ChoiceStatColumnWidth = 6;

    // Screen-space distance the block is pushed down into the card node's top padding, per screen.
    // Reward screen felt right at 40; the large inspect card needs less.
    private const float StackedDownOffset = 40f;
    private const float WideDownOffset = 14f;
    private const float UpgradeDownOffset = -14f; // negative = higher (above the card node top)
    private const float ShopDownOffset = 58f;
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
    private static readonly HashSet<RealtimeEvService.CardEvResult> passSeen = new();
    // Cards already rendered this tracked pass - the reward screen walks holders AND descendants, so
    // the same card is visited twice; skip the 2nd (only while a poll pass is active).
    private static readonly HashSet<NCard> passRenderedCards = new();
    private static bool settleTrackingActive;
    private static int passRealtimeTotal;
    private static int passRealtimeSettled;
    private static double passProgressSum;

    public static void BeginSettleTracking()
    {
        passSeen.Clear();
        passRenderedCards.Clear();
        settleTrackingActive = true;
        passRealtimeTotal = 0;
        passRealtimeSettled = 0;
        passProgressSum = 0d;
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
        passRealtimeTotal <= 0 ? 0d : passProgressSum / passRealtimeTotal;

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
        passProgressSum += result.ProgressFraction;
        if (result.IsSettled)
        {
            passRealtimeSettled++;
        }
    }

    // --- universal overlay refresh pump (main thread only) ---
    // Root fix for the "screen stays on ..." bug. The overlay renders on GAME-driven events
    // (NCard.UpdateVisuals, screen SetCard hooks), but the EV result lands asynchronously seconds
    // later. Screens with a dedicated poll scheduler (reward, upgrade preview) re-render themselves
    // until settled; screens WITHOUT one - the inspect / deck-detail card - rendered once and got
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

    // Set by ResolveRealtimeDelta each render so Render (via UpdatePumpAfterRender) knows whether an
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
        CardOverlayDisplayContext displayContext = CardOverlayContext.ResolveDisplayContext(cardNode, contextRoot);

        lastRenderRequestedLive = false;
        lastResolvedLiveResult = null;
        string? text = ResolveOverlayText(config, cardNode, forcedUpgradeState, displayContext);
        if (string.IsNullOrWhiteSpace(text))
        {
            if (existing is not null)
            {
                existing.Visible = false;
            }

            UnregisterPump(cardNode);
            return;
        }

        int fontSize = displayContext switch
        {
            CardOverlayDisplayContext.UpgradePreview => UpgradeFontSize,
            CardOverlayDisplayContext.Shop => ShopFontSize,
            _ => FontSize
        };
        float width = displayContext switch
        {
            CardOverlayDisplayContext.Inspect or CardOverlayDisplayContext.InspectAdd => WideWidth,
            CardOverlayDisplayContext.UpgradePreview => UpgradeWidth,
            CardOverlayDisplayContext.Shop => ShopWidth,
            _ => StackedWidth
        };
        float downOffset = displayContext switch
        {
            CardOverlayDisplayContext.Inspect or CardOverlayDisplayContext.InspectAdd => WideDownOffset,
            CardOverlayDisplayContext.UpgradePreview => UpgradeDownOffset,
            CardOverlayDisplayContext.Shop => ShopDownOffset,
            _ => StackedDownOffset
        };

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

    public static void RenderShopInventory(NMerchantInventory inventory)
    {
        RenderDescendantCards(inventory, inventory);
    }

    public static void Hide(NCard card)
    {
        RichTextLabel? label = GetExistingLabel(card);
        if (label is not null)
        {
            label.Visible = false;
        }

        UnregisterPump(card);
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
    private const float UpgradeDeltaWidth = 330f;
    private const int UpgradeDeltaFontSize = 14;
    private const float UpgradeTableHorizontalGap = 12f;
    private const int StackedTableLineCount = 9; // dEV table + blank line + card-choice table

    // Upgrade preview: a small table between the cards showing the direct deck delta from
    // replacing the current unupgraded copy with its upgraded form.
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
            RealtimeEvService.CardEnchantmentRef? enchantment =
                RealtimeEvService.ReadCardEnchantment(before.Model);
            RealtimeEvService.CardEvResult? upgradeResult =
                RealtimeEvService.RequestUpgradeEv(cardKey, enchantment);
            if (upgradeResult is not null)
            {
                NoteRealtimeResult(upgradeResult);
            }

            RichTextLabel label = GetOrCreateDeltaLabel(previewRoot);
            string deltaText = BuildDeltaTable("dEV", upgradeResult);
            label.Text = deltaText;
            label.AddThemeFontSizeOverride("normal_font_size", UpgradeDeltaFontSize);
            SetContentSize(label, deltaText, UpgradeDeltaWidth, UpgradeDeltaFontSize);
            label.Visible = true;

            float lineHeight = GetMonospaceFont().GetHeight(UpgradeFontSize);
            float tablesHeight = StackedTableLineCount * lineHeight + StyleboxVerticalMargin;
            Vector2 beforeAnchor = TableAnchor(before, UpgradeDownOffset);
            Vector2 afterAnchor = TableAnchor(after, UpgradeDownOffset);
            float midX = (beforeAnchor.X + afterAnchor.X) * 0.5f;
            float tablesCenterY = beforeAnchor.Y - tablesHeight / 2f;
            label.GlobalPosition = new Vector2(
                midX - label.Size.X / 2f,
                tablesCenterY - label.Size.Y / 2f);
            SeparateUpgradePreviewTables(before, after, label);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"Failed to render upgrade dEV: {ex.Message}", 0);
        }
    }

    private static void SeparateUpgradePreviewTables(
        NCard before,
        NCard after,
        RichTextLabel deltaLabel)
    {
        float deltaLeft = deltaLabel.GlobalPosition.X;
        float deltaRight = deltaLeft + deltaLabel.Size.X;

        RichTextLabel? beforeLabel = GetExistingLabel(before);
        if (beforeLabel is { Visible: true })
        {
            float overlap = beforeLabel.GlobalPosition.X + beforeLabel.Size.X
                - (deltaLeft - UpgradeTableHorizontalGap);
            if (overlap > 0f)
            {
                beforeLabel.GlobalPosition += new Vector2(-overlap, 0f);
            }
        }

        RichTextLabel? afterLabel = GetExistingLabel(after);
        if (afterLabel is { Visible: true })
        {
            float overlap = deltaRight + UpgradeTableHorizontalGap - afterLabel.GlobalPosition.X;
            if (overlap > 0f)
            {
                afterLabel.GlobalPosition += new Vector2(overlap, 0f);
            }
        }
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

    // A single "calculating ###-- 42%" bar centered horizontally at topFraction of the screen height.
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
            string plain = $"calculating {new string('#', filled)}{new string('-', ProgressBarSegments - filled)} {pct}%";
            string bbcode =
                $"calculating [color={GreenColor}]{new string('#', filled)}[/color]"
                + $"[color={GrayColor}]{new string('-', ProgressBarSegments - filled)}[/color] {pct}%";

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

    private static string? ResolveOverlayText(
        CardValueConfig config,
        NCard cardNode,
        CardUpgradeState? forcedUpgradeState,
        CardOverlayDisplayContext displayContext)
    {
        OverlaySettings settings = config.Overlay;
        return settings.DisplayMode switch
        {
            OverlayDisplayMode.FixedText => ResolveFixedText(settings),
            OverlayDisplayMode.CardName => ResolveCardName(cardNode),
            OverlayDisplayMode.TrainingValue => ResolveRealtimeDelta(cardNode, forcedUpgradeState, displayContext),
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

    private static string? ResolveRealtimeDelta(
        NCard cardNode,
        CardUpgradeState? forcedUpgradeState,
        CardOverlayDisplayContext displayContext)
    {
        if (cardNode.Model is null)
        {
            return null;
        }

        lastRenderRequestedLive = true;

        bool deckView = displayContext == CardOverlayDisplayContext.Inspect;
        bool upgradePreview = displayContext == CardOverlayDisplayContext.UpgradePreview;
        bool shop = displayContext == CardOverlayDisplayContext.Shop;
        string cardKey = cardNode.Model.Id.ToString();
        RealtimeEvService.CardEnchantmentRef? enchantment =
            RealtimeEvService.ReadCardEnchantment(cardNode.Model);
        CardUpgradeState upgradeState = forcedUpgradeState
            ?? (cardNode.Model.CurrentUpgradeLevel > 0
                ? CardUpgradeState.Upgraded
                : CardUpgradeState.Unupgraded);
        int probeUpgrade = upgradeState == CardUpgradeState.Upgraded ? 1 : 0;
        int? removeUpgrade = upgradePreview ? 0 : deckView ? probeUpgrade : null;
        RealtimeEvService.CardEvResult? result = RealtimeEvService.RequestCardEv(
            cardKey,
            probeUpgrade,
            removeUpgrade,
            enchantment);
        lastResolvedLiveResult = result;

        if (result is not null)
        {
            NoteRealtimeResult(result);
        }

        string basisLabel = deckView
            ? "owned dEV"
            : upgradePreview
                ? "form dEV"
                : "dEV";
        CardAdoptionDisplayStats? adoption = CardAdoptionStatsProvider.Resolve(cardKey, upgradeState);
        return string.Join('\n',
        [
            BuildDeltaTable(basisLabel, result),
            "",
            BuildCardChoiceTable(adoption, upgradeState, shop)
        ]);
    }

    private static string BuildDeltaTable(
        string basisLabel,
        RealtimeEvService.CardEvResult? result)
    {
        (string Label, string Mean, string Interval, string Runs, double? MeanValue, string RunsColor) Cell(
            string label,
            RealtimeEvService.HorizonDeltaResult? horizon)
        {
            if (result?.Failed == true)
            {
                return (label, "n/a", "n/a", BuildRunsCell(result, horizon), null, ErrorColor);
            }

            if (result is null || horizon is null)
            {
                return (label, "...", "...", BuildRunsCell(result, horizon), null, GrayColor);
            }

            string mean = horizon.Mean.ToString("+0.#;-0.#;0", CultureInfo.InvariantCulture);
            string lower = horizon.LowerConfidence.ToString("+0.#;-0.#;0", CultureInfo.InvariantCulture);
            string upper = horizon.UpperConfidence.ToString("+0.#;-0.#;0", CultureInfo.InvariantCulture);
            return (
                label,
                mean,
                $"[{lower},{upper}]",
                BuildRunsCell(result, horizon),
                horizon.Mean,
                ResolveRunsColor(horizon));
        }

        (string Label, string Mean, string Interval, string Runs, double? MeanValue, string RunsColor)[] cells =
        [
            Cell("short", result?.Short),
            Cell("mid", result?.Mid),
            Cell("long", result?.Long)
        ];
        int labelWidth = Math.Max(basisLabel.Length, cells.Max(cell => cell.Label.Length));
        int meanWidth = Math.Max(MeanColumnWidth, cells.Max(cell => cell.Mean.Length));
        int intervalWidth = Math.Max(IntervalColumnWidth, cells.Max(cell => cell.Interval.Length));
        int runsWidth = Math.Max(RunsColumnWidth, cells.Max(cell => cell.Runs.Length));

        string Row((string Label, string Mean, string Interval, string Runs, double? MeanValue, string RunsColor) cell)
        {
            string paddedMean = cell.Mean.PadLeft(meanWidth);
            string coloredMean = cell.MeanValue switch
            {
                > 0d => $"[color={GreenColor}]{paddedMean}[/color]",
                < 0d => $"[color={RedColor}]{paddedMean}[/color]",
                null => $"[color={GrayColor}]{paddedMean}[/color]",
                _ => paddedMean
            };
            string interval = cell.Interval.PadLeft(intervalWidth);
            string runs = cell.Runs.PadLeft(runsWidth);
            return $"{cell.Label.PadRight(labelWidth)} {coloredMean} "
                + $"{interval} [color={cell.RunsColor}]{runs}[/color]";
        }

        string confidenceHeader = $"{CardValueOverlayModConfig.CurrentSettings.ConfidenceLevelPercent}% CI";
        return string.Join('\n',
        [
            $"{basisLabel.PadRight(labelWidth)} {"mean".PadLeft(meanWidth)} "
                + $"{confidenceHeader.PadLeft(intervalWidth)} {"runs".PadLeft(runsWidth)}",
            .. cells.Select(Row)
        ]);
    }

    private static string ResolveRunsColor(RealtimeEvService.HorizonDeltaResult horizon)
    {
        if (horizon.State == RealtimeEvService.SamplingState.Preview)
        {
            return GrayColor;
        }

        if (horizon.State == RealtimeEvService.SamplingState.Stable)
        {
            return GreenColor;
        }

        bool marginalIntervalHasStableSign =
            horizon.LowerConfidence > 0d || horizon.UpperConfidence < 0d;
        return marginalIntervalHasStableSign ? YellowColor : RedColor;
    }

    private static string BuildRunsCell(
        RealtimeEvService.CardEvResult? result,
        RealtimeEvService.HorizonDeltaResult? horizon)
    {
        if (result?.Failed == true)
        {
            return $"n{horizon?.CompletedRuns ?? 0}!";
        }

        if (result is null || horizon is null)
        {
            return "n0~";
        }

        string suffix = horizon.State switch
        {
            RealtimeEvService.SamplingState.Stable => "ok",
            RealtimeEvService.SamplingState.MaxUncertain => "?",
            RealtimeEvService.SamplingState.Preview
                or RealtimeEvService.SamplingState.Refining => "~",
            _ => ""
        };
        return $"n{horizon.CompletedRuns}{suffix}";
    }

    private static string BuildCardChoiceTable(
        CardAdoptionDisplayStats? adoption,
        CardUpgradeState upgradeState,
        bool useShopBuyRate)
    {
        bool known = adoption is not null;
        string choiceLabel = useShopBuyRate
            ? upgradeState == CardUpgradeState.Upgraded ? "buy +1" : "buy +0"
            : upgradeState == CardUpgradeState.Upgraded ? "pick +1" : "pick +0";
        double? choiceRate = useShopBuyRate ? adoption?.ShopBuyRate : adoption?.PickRate;
        CardAdoptionStatBand choiceBand = useShopBuyRate
            ? adoption?.ShopBuyRateBand ?? CardAdoptionStatBand.Unknown
            : adoption?.PickRateBand ?? CardAdoptionStatBand.Unknown;
        (string Label, string Value, CardAdoptionStatBand Band, bool UpsideOnly)[] rows =
        [
            ("deck", FormatPercent(adoption?.AppearanceProbability, known),
                adoption?.AppearanceBand ?? CardAdoptionStatBand.Unknown, false),
            (choiceLabel, FormatPercent(choiceRate, known), choiceBand, false),
            ("copies", FormatCopies(adoption?.AvgCopiesWhenPresent, known),
                adoption?.AvgCopiesWhenPresentBand ?? CardAdoptionStatBand.Unknown, true)
        ];
        int labelWidth = Math.Max(ChoiceLabelColumnWidth, rows.Max(row => row.Label.Length));
        int valueWidth = Math.Max(ChoiceStatColumnWidth, rows.Max(row => row.Value.Length));

        string Row((string Label, string Value, CardAdoptionStatBand Band, bool UpsideOnly) row)
        {
            string plain = $"{row.Label.PadRight(labelWidth)} {row.Value.PadLeft(valueWidth)}";
            return row.UpsideOnly
                ? ColorByAdoptionUpside(plain, row.Band)
                : ColorByAdoptionBand(plain, row.Band);
        }

        return string.Join('\n',
        [
            $"{"choice".PadRight(labelWidth)} {"stats".PadLeft(valueWidth)}",
            .. rows.Select(Row)
        ]);
    }

    private static string FormatPercent(double? value, bool known)
    {
        if (!known)
        {
            return "--";
        }

        return value is double resolved
            ? $"{(resolved * 100d).ToString("0.#", CultureInfo.InvariantCulture)}%"
            : "n/a";
    }

    private static string FormatCopies(double? value, bool known)
    {
        if (!known)
        {
            return "--";
        }

        return value is double resolved
            ? resolved.ToString("0.00", CultureInfo.InvariantCulture)
            : "n/a";
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

    // A monospace system font so the dEV confidence columns line up (a proportional font makes
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

    internal const string GreenColor = "#5fd35f";
    internal const string RedColor = "#ef5f5f";
    internal const string YellowColor = "#f0c84b";
    internal const string GrayColor = "#b9bfc6";
    internal const string ErrorColor = "#d36bff";

    private static string ColorByAdoptionBand(string paddedText, CardAdoptionStatBand band)
    {
        return band switch
        {
            CardAdoptionStatBand.High => $"[color={GreenColor}]{paddedText}[/color]",
            CardAdoptionStatBand.Low => $"[color={RedColor}]{paddedText}[/color]",
            CardAdoptionStatBand.Unknown => $"[color={GrayColor}]{paddedText}[/color]",
            _ => paddedText
        };
    }

    private static string ColorByAdoptionUpside(string paddedText, CardAdoptionStatBand band)
    {
        return band switch
        {
            CardAdoptionStatBand.High => $"[color={GreenColor}]{paddedText}[/color]",
            CardAdoptionStatBand.Unknown => $"[color={GrayColor}]{paddedText}[/color]",
            _ => paddedText
        };
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
