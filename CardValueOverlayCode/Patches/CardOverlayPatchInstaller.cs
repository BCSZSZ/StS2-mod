using System.Reflection;
using CardValueOverlay.CardValueOverlayCode.Overlay;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;

namespace CardValueOverlay.CardValueOverlayCode.Patches;

public static class CardOverlayPatchInstaller
{
    private static readonly FieldInfo? InspectCardField = AccessTools.Field(typeof(NInspectCardScreen), "_card");

    public static void Install(Harmony harmony)
    {
        PatchOptional(harmony, typeof(NInspectCardScreen), "SetCard", nameof(RenderInspectCardScreenPostfix));
        PatchOptional(harmony, typeof(NInspectCardScreen), "UpdateCardDisplay", nameof(RenderInspectCardScreenPostfix));
        PatchOptional(
            harmony,
            typeof(NCardRewardSelectionScreen),
            "_EnterTree",
            nameof(ScheduleRewardScreenRefreshPostfix));
        PatchOptional(
            harmony,
            typeof(NCardRewardSelectionScreen),
            "_Ready",
            nameof(ScheduleRewardScreenRefreshPostfix));
        PatchOptional(
            harmony,
            typeof(NCardRewardSelectionScreen),
            nameof(NCardRewardSelectionScreen.RefreshOptions),
            nameof(ScheduleRewardScreenRefreshPostfix));
        PatchOptional(
            harmony,
            typeof(NCardRewardSelectionScreen),
            "AfterOverlayOpened",
            nameof(ScheduleRewardScreenRefreshPostfix));
        PatchOptional(
            harmony,
            typeof(NCardRewardSelectionScreen),
            "AfterOverlayShown",
            nameof(ScheduleRewardScreenRefreshPostfix));
        PatchOptional(
            harmony,
            typeof(NUpgradePreview),
            "Reload",
            nameof(ScheduleUpgradePreviewRefreshPostfix));
    }

    private static void PatchOptional(Harmony harmony, Type targetType, string targetMethodName, string postfixMethodName)
    {
        try
        {
            MethodInfo? target = AccessTools.Method(targetType, targetMethodName);
            MethodInfo? postfix = AccessTools.Method(typeof(CardOverlayPatchInstaller), postfixMethodName);

            if (target is null || postfix is null)
            {
                MainFile.Logger.Warn(
                    $"Skipped optional overlay refresh patch: {targetType.FullName}.{targetMethodName}",
                    0);
                return;
            }

            harmony.Patch(target, postfix: new HarmonyMethod(postfix));
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn(
                $"Failed optional overlay refresh patch {targetType.FullName}.{targetMethodName}: {ex.Message}",
                0);
        }
    }

    private static void RenderInspectCardScreenPostfix(NInspectCardScreen __instance)
    {
        try
        {
            NCard? card = InspectCardField?.GetValue(__instance) as NCard;
            CardOverlayRenderer.RenderInspectScreen(__instance, card);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"Failed to refresh inspect card overlay: {ex.Message}", 0);
        }
    }

    private static void ScheduleRewardScreenRefreshPostfix(NCardRewardSelectionScreen __instance)
    {
        try
        {
            RewardScreenOverlayRefreshScheduler.Schedule(__instance);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"Failed to refresh reward card overlays: {ex.Message}", 0);
        }
    }

    private static void ScheduleUpgradePreviewRefreshPostfix(NUpgradePreview __instance)
    {
        try
        {
            UpgradePreviewOverlayRefreshScheduler.Schedule(__instance);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"Failed to refresh upgrade preview overlays: {ex.Message}", 0);
        }
    }
}
