using CardValueOverlay.CardValueOverlayCode.Overlay;
using CardValueOverlay.CardValueOverlayCode.Runtime;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;

namespace CardValueOverlay.CardValueOverlayCode.Patches;

public static class ShopOverlayRefreshScheduler
{
    private static ulong scheduledInventoryId;

    private static readonly double[] RefreshDelays =
    [
        0.0,
        0.05,
        0.15,
        0.35,
        0.70,
        0.90
    ];

    public static void Schedule(NMerchantInventory inventory)
    {
        RealtimeEvService.Prefetch();
        ulong inventoryId = inventory.GetInstanceId();
        if (scheduledInventoryId == inventoryId)
        {
            // Initialize/Open/Purchase hooks can target the same inventory. Coalesce their timer
            // bursts; an immediate refresh is sufficient for the state change.
            Refresh(inventory);
            return;
        }

        scheduledInventoryId = inventoryId;
        foreach (double delay in RefreshDelays)
        {
            ScheduleOne(inventory, delay);
        }
    }

    private static void ScheduleOne(NMerchantInventory inventory, double delaySeconds)
    {
        if (delaySeconds <= 0.0)
        {
            Refresh(inventory);
            return;
        }

        SceneTree? tree = inventory.GetTree();
        if (tree is null)
        {
            return;
        }

        SceneTreeTimer timer = tree.CreateTimer(delaySeconds);
        timer.Timeout += () => Refresh(inventory);
    }

    private static void Refresh(NMerchantInventory inventory)
    {
        try
        {
            if (!GodotObject.IsInstanceValid(inventory) || !inventory.IsInsideTree())
            {
                return;
            }

            CardOverlayRenderer.RenderShopInventory(inventory);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"Failed scheduled shop overlay refresh: {ex.Message}", 0);
        }
    }
}
