using Godot;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using System.Runtime.CompilerServices;

namespace CardValueOverlay.CardValueOverlayCode.Overlay;

public static class CardOverlayContext
{
    private sealed class InspectContextState
    {
        public CardOverlayDisplayContext DisplayContext { get; set; } = CardOverlayDisplayContext.Inspect;
    }

    private static readonly ConditionalWeakTable<NInspectCardScreen, InspectContextState> InspectContexts = new();

    public static void SetInspectCardOwnership(NInspectCardScreen screen, bool isCurrentDeckCard)
    {
        InspectContexts.GetOrCreateValue(screen).DisplayContext = isCurrentDeckCard
            ? CardOverlayDisplayContext.Inspect
            : CardOverlayDisplayContext.InspectAdd;
    }

    public static bool ShouldShowFor(Node cardNode)
    {
        return TryGetDisplayContext(cardNode, out _);
    }

    public static bool TryGetDisplayContext(Node node, out Node? contextRoot)
    {
        contextRoot = FindAncestor<NInspectCardScreen>(node);
        contextRoot ??= FindAncestor<NCardRewardSelectionScreen>(node);
        contextRoot ??= FindAncestor<NUpgradePreview>(node);
        contextRoot ??= FindAncestor<NMerchantInventory>(node);

        return contextRoot is not null;
    }

    public static CardOverlayDisplayContext ResolveDisplayContext(Node node, Node? contextRoot)
    {
        Node? resolved = contextRoot;
        if (resolved is null && !TryGetDisplayContext(node, out resolved))
        {
            return CardOverlayDisplayContext.None;
        }

        return resolved switch
        {
            NInspectCardScreen inspect => InspectContexts.TryGetValue(inspect, out InspectContextState? state)
                ? state.DisplayContext
                : CardOverlayDisplayContext.Inspect,
            NCardRewardSelectionScreen => CardOverlayDisplayContext.Reward,
            NUpgradePreview => CardOverlayDisplayContext.UpgradePreview,
            NMerchantInventory => CardOverlayDisplayContext.Shop,
            _ => CardOverlayDisplayContext.None
        };
    }

    private static TNode? FindAncestor<TNode>(Node node) where TNode : Node
    {
        for (Node? current = node; current is not null; current = current.GetParent())
        {
            if (current is TNode match)
            {
                return match;
            }
        }

        return null;
    }
}

public enum CardOverlayDisplayContext
{
    None,
    Reward,
    Shop,
    Inspect,
    InspectAdd,
    UpgradePreview
}
