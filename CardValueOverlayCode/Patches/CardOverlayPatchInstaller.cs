using System.Reflection;
using CardValueOverlay.CardValueOverlayCode.Overlay;
using CardValueOverlay.CardValueOverlayCode.Runtime;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;

namespace CardValueOverlay.CardValueOverlayCode.Patches;

public static class CardOverlayPatchInstaller
{
    private static readonly FieldInfo? InspectCardField = AccessTools.Field(typeof(NInspectCardScreen), "_card");
    private static readonly FieldInfo? InspectCardsField = AccessTools.Field(typeof(NInspectCardScreen), "_cards");
    private static readonly FieldInfo? MerchantCardNodeField = AccessTools.Field(typeof(NMerchantCard), "_cardNode");

    public static void Install(Harmony harmony)
    {
        PatchOptionalPrefix(harmony, typeof(NInspectCardScreen), "SetCard", nameof(PrepareInspectCardBasisPrefix));
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
        PatchOptional(
            harmony,
            typeof(NMerchantInventory),
            nameof(NMerchantInventory.Initialize),
            nameof(ScheduleShopRefreshPostfix));
        PatchOptional(
            harmony,
            typeof(NMerchantInventory),
            nameof(NMerchantInventory.Open),
            nameof(ScheduleShopRefreshPostfix));
        PatchOptional(
            harmony,
            typeof(NMerchantInventory),
            "OnPurchaseCompleted",
            nameof(ScheduleShopRefreshPostfix));
        PatchOptionalPrefix(
            harmony,
            typeof(NMerchantCard),
            "OnSuccessfulPurchase",
            nameof(HideShopCardBeforePurchasePrefix));
        PatchOptional(
            harmony,
            typeof(NEventOptionButton),
            "_Ready",
            nameof(RenderAncientChoiceButtonPostfix));
    }

    private static void PatchOptional(Harmony harmony, Type targetType, string targetMethodName, string postfixMethodName)
    {
        PatchOptionalCore(harmony, targetType, targetMethodName, postfixMethodName, usePrefix: false);
    }

    private static void PatchOptionalPrefix(Harmony harmony, Type targetType, string targetMethodName, string prefixMethodName)
    {
        PatchOptionalCore(harmony, targetType, targetMethodName, prefixMethodName, usePrefix: true);
    }

    private static void PatchOptionalCore(
        Harmony harmony,
        Type targetType,
        string targetMethodName,
        string patchMethodName,
        bool usePrefix)
    {
        try
        {
            MethodInfo? target = AccessTools.Method(targetType, targetMethodName);
            MethodInfo? patch = AccessTools.Method(typeof(CardOverlayPatchInstaller), patchMethodName);

            if (target is null || patch is null)
            {
                MainFile.Logger.Warn(
                    $"Skipped optional overlay refresh patch: {targetType.FullName}.{targetMethodName}",
                    0);
                return;
            }

            HarmonyMethod harmonyMethod = new(patch);
            if (usePrefix)
            {
                harmony.Patch(target, prefix: harmonyMethod);
            }
            else
            {
                harmony.Patch(target, postfix: harmonyMethod);
            }
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

    private static void PrepareInspectCardBasisPrefix(NInspectCardScreen __instance, int index)
    {
        try
        {
            if (InspectCardsField?.GetValue(__instance) is not IReadOnlyList<CardModel> cards || cards.Count == 0)
            {
                return;
            }

            CardModel sourceCard = cards[Math.Clamp(index, 0, cards.Count - 1)];
            CardOverlayContext.SetInspectCardOwnership(
                __instance,
                RealtimeEvService.IsCurrentDeckCardInstance(sourceCard));
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"Failed to resolve inspect card calculation basis: {ex.Message}", 0);
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

    private static void ScheduleShopRefreshPostfix(NMerchantInventory __instance)
    {
        try
        {
            ShopOverlayRefreshScheduler.Schedule(__instance);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"Failed to refresh shop card overlays: {ex.Message}", 0);
        }
    }

    private static void HideShopCardBeforePurchasePrefix(NMerchantCard __instance)
    {
        try
        {
            if (MerchantCardNodeField?.GetValue(__instance) is NCard card)
            {
                CardOverlayRenderer.Hide(card);
            }
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"Failed to hide purchased shop card overlay: {ex.Message}", 0);
        }
    }

    private static void RenderAncientChoiceButtonPostfix(NEventOptionButton __instance)
    {
        try
        {
            AncientChoiceOverlayRenderer.Render(__instance);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"Failed to refresh ancient choice overlay: {ex.Message}", 0);
        }
    }
}
