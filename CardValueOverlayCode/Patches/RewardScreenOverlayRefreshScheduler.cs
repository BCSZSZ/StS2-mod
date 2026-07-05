using CardValueOverlay.CardValueOverlayCode.Overlay;
using CardValueOverlay.CardValueOverlayCode.Runtime;
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

    // While the background EV simulation is still running, keep re-rendering so the
    // "calculating..." cells fill in as results land. Poll every this many seconds up to
    // a hard cap (safety against a stuck computation).
    private const double PendingPollInterval = 0.5;
    private const double PendingPollCap = 60.0;

    public static void Schedule(NCardRewardSelectionScreen screen)
    {
        // Warm the modeling data/library on a background thread as soon as the reward screen opens,
        // so the first card's EV compute doesn't also pay the one-time load/build spike.
        RealtimeEvService.Prefetch();

        foreach (double delay in RefreshDelays)
        {
            ScheduleOne(screen, delay);
        }

        SchedulePendingPoll(screen, PendingPollInterval);
    }

    private static void SchedulePendingPoll(NCardRewardSelectionScreen screen, double elapsed)
    {
        SceneTree? tree = screen.GetTree();
        if (tree is null)
        {
            return;
        }

        SceneTreeTimer timer = tree.CreateTimer(PendingPollInterval);
        timer.Timeout += () =>
        {
            if (!GodotObject.IsInstanceValid(screen) || !screen.IsInsideTree())
            {
                return;
            }

            Refresh(screen);
            if (elapsed < PendingPollCap && RealtimeEvService.HasPendingWork)
            {
                SchedulePendingPoll(screen, elapsed + PendingPollInterval);
            }
        };
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
