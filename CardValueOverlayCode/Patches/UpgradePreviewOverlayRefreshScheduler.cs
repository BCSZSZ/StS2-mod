using System.Reflection;
using CardValueOverlay.Core.Configuration;
using CardValueOverlay.CardValueOverlayCode.Overlay;
using CardValueOverlay.CardValueOverlayCode.Runtime;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Cards;

namespace CardValueOverlay.CardValueOverlayCode.Patches;

// Renders the training-value overlay above both cards of an upgrade preview (NUpgradePreview):
// the "_before" card as unupgraded and the "_after" preview card as upgraded. Uses staggered
// deferred refreshes because the preview cards need a few frames to lay out before their global
// transform (used to position the label) is stable.
public static class UpgradePreviewOverlayRefreshScheduler
{
    private static readonly FieldInfo? BeforeField = AccessTools.Field(typeof(NUpgradePreview), "_before");
    private static readonly FieldInfo? AfterField = AccessTools.Field(typeof(NUpgradePreview), "_after");

    private static readonly double[] RefreshDelays =
    [
        0.0,
        0.02,
        0.05,
        0.10,
        0.18,
        0.30,
        0.50
    ];

    // Keep re-rendering while the background EV sim is still running, so the "..." cells fill in
    // once results land (the initial burst above ends at 0.5s, long before the sim finishes).
    private const double PendingPollInterval = 0.5;
    private const double PendingPollCap = 60.0;

    public static void Schedule(NUpgradePreview preview)
    {
        RealtimeEvService.Prefetch();
        foreach (double delay in RefreshDelays)
        {
            ScheduleOne(preview, delay);
        }

        SchedulePendingPoll(preview, PendingPollInterval);
    }

    private static void SchedulePendingPoll(NUpgradePreview preview, double elapsed)
    {
        SceneTree? tree = preview.GetTree();
        if (tree is null)
        {
            return;
        }

        SceneTreeTimer timer = tree.CreateTimer(PendingPollInterval);
        timer.Timeout += () =>
        {
            if (!GodotObject.IsInstanceValid(preview) || !preview.IsInsideTree())
            {
                return;
            }

            Refresh(preview);
            if (elapsed < PendingPollCap && RealtimeEvService.HasPendingWork)
            {
                SchedulePendingPoll(preview, elapsed + PendingPollInterval);
            }
        };
    }

    private static void ScheduleOne(NUpgradePreview preview, double delaySeconds)
    {
        if (delaySeconds <= 0.0)
        {
            Refresh(preview);
            return;
        }

        SceneTree? tree = preview.GetTree();
        if (tree is null)
        {
            return;
        }

        SceneTreeTimer timer = tree.CreateTimer(delaySeconds);
        timer.Timeout += () => Refresh(preview);
    }

    private static void Refresh(NUpgradePreview preview)
    {
        try
        {
            if (!GodotObject.IsInstanceValid(preview) || !preview.IsInsideTree())
            {
                return;
            }

            Control? before = BeforeField?.GetValue(preview) as Control;
            Control? after = AfterField?.GetValue(preview) as Control;

            if (before is not null)
            {
                CardOverlayRenderer.RenderCardsWithForcedState(before, preview, CardUpgradeState.Unupgraded);
            }

            if (after is not null)
            {
                CardOverlayRenderer.RenderCardsWithForcedState(after, preview, CardUpgradeState.Upgraded);
            }

            // The improvement table (upgraded - unupgraded) shown between the two cards.
            if (before is not null && after is not null)
            {
                CardOverlayRenderer.RenderUpgradeDelta(preview, before, after);
            }
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"Failed scheduled upgrade preview overlay refresh: {ex.Message}", 0);
        }
    }
}
