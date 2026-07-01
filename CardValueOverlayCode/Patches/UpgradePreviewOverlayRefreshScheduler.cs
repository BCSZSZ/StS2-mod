using System.Reflection;
using CardValueOverlay.Core.Configuration;
using CardValueOverlay.CardValueOverlayCode.Overlay;
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

    public static void Schedule(NUpgradePreview preview)
    {
        foreach (double delay in RefreshDelays)
        {
            ScheduleOne(preview, delay);
        }
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

            if (BeforeField?.GetValue(preview) is Control before)
            {
                CardOverlayRenderer.RenderCardsWithForcedState(before, preview, CardUpgradeState.Unupgraded);
            }

            if (AfterField?.GetValue(preview) is Control after)
            {
                CardOverlayRenderer.RenderCardsWithForcedState(after, preview, CardUpgradeState.Upgraded);
            }
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"Failed scheduled upgrade preview overlay refresh: {ex.Message}", 0);
        }
    }
}
