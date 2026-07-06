// Deck-view EV is now computed ON DEMAND. Opening the deck grid no longer bulk-precomputes every
// card (that queued 20+ serial tasks and stalled the single worker). The grid itself never computes
// (grid cards fail ShouldShowFor), so nothing runs until you INSPECT (click) a card - which computes
// just that card, in both its current and other upgrade form (see the deck-view branch of
// CardOverlayRenderer.ResolveTrainingValue). The former NDeckViewScreen._Ready ->
// RealtimeEvService.PrecomputeDeckCards Harmony patch was removed for this reason.
namespace CardValueOverlay.CardValueOverlayCode.Patches;
