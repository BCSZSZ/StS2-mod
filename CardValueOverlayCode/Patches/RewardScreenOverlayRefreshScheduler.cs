using CardValueOverlay.CardValueOverlayCode.Overlay;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;

namespace CardValueOverlay.CardValueOverlayCode.Patches;

public static class RewardScreenOverlayRefreshScheduler
{
    private static readonly double[] RefreshDelays =
    [
        0.0,
        0.02,
        0.05,
        0.10,
        0.18,
        0.30,
        0.50,
        0.75,
        1.00
    ];

    public static void Schedule(NCardRewardSelectionScreen screen)
    {
        foreach (double delay in RefreshDelays)
        {
            ScheduleOne(screen, delay);
        }
    }

    private static void ScheduleOne(NCardRewardSelectionScreen screen, double delaySeconds)
    {
        if (delaySeconds <= 0.0)
        {
            Refresh(screen);
            return;
        }

        SceneTree? tree = screen.GetTree();
        if (tree is null)
        {
            return;
        }

        SceneTreeTimer timer = tree.CreateTimer(delaySeconds);
        timer.Timeout += () => Refresh(screen);
    }

    private static void Refresh(NCardRewardSelectionScreen screen)
    {
        try
        {
            if (!GodotObject.IsInstanceValid(screen) || !screen.IsInsideTree())
            {
                return;
            }

            CardOverlayRenderer.RenderRewardScreen(screen);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"Failed scheduled reward overlay refresh: {ex.Message}", 0);
        }
    }
}
