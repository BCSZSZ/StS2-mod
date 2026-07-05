using Godot;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;

namespace CardValueOverlay.CardValueOverlayCode.Overlay;

public static class CardOverlayContext
{
    public static bool ShouldShowFor(Node cardNode)
    {
        return TryGetDisplayContext(cardNode, out _);
    }

    public static bool TryGetDisplayContext(Node node, out Node? contextRoot)
    {
        contextRoot = FindAncestor<NInspectCardScreen>(node);
        contextRoot ??= FindAncestor<NCardRewardSelectionScreen>(node);

        return contextRoot is not null;
    }

    // Deck/inspect view (single large card, horizontal space available) vs. the narrow
    // reward screen (three cards side by side). Drives the overlay's wide vs. stacked layout.
    public static bool IsInspectContext(Node node)
    {
        return FindAncestor<NInspectCardScreen>(node) is not null;
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
