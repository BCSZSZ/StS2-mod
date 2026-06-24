using CardValueOverlay.CardValueOverlayCode.Overlay;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Cards;

namespace CardValueOverlay.CardValueOverlayCode.Patches;

[HarmonyPatch(typeof(NCard), nameof(NCard.UpdateVisuals))]
public static class NCardUpdateVisualsPatch
{
    public static void Postfix(NCard __instance)
    {
        try
        {
            CardOverlayRenderer.Render(__instance);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"Failed to render card overlay: {ex.Message}", 0);
        }
    }
}
