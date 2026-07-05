using CardValueOverlay.CardValueOverlayCode.Runtime;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Screens;

namespace CardValueOverlay.CardValueOverlayCode.Patches;

// Value every card in the deck as soon as the deck-view screen opens, so browsing the deck is
// instant (each card fills in the background; results are cached per deck signature).
[HarmonyPatch(typeof(NDeckViewScreen), nameof(NDeckViewScreen._Ready))]
public static class DeckViewPrecomputePatch
{
    public static void Postfix()
    {
        try
        {
            RealtimeEvService.PrecomputeDeckCards();
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"Deck-view precompute failed: {ex.Message}", 0);
        }
    }
}
