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

    // Instance id of the screen whose poll chain is currently running. The reward screen fires 5
    // patched hooks (EnterTree/_Ready/RefreshOptions/AfterOverlay*), each calling Schedule; without
    // this guard each would spawn its own parallel poll chain. 0 = no chain active.
    private static ulong pollingScreenId;

    public static void Schedule(NCardRewardSelectionScreen screen)
    {
        // Warm the modeling data/library on a background thread as soon as the reward screen opens,
        // so the first card's EV compute doesn't also pay the one-time load/build spike.
        RealtimeEvService.Prefetch();

        foreach (double delay in RefreshDelays)
        {
            ScheduleOne(screen, delay);
        }

        // Start at most one pending-poll chain per screen instance. chainId is captured now (a plain
        // value) so releasing later never touches a possibly-freed screen and never clobbers a newer
        // screen's chain.
        ulong chainId = screen.GetInstanceId();
        if (pollingScreenId == chainId)
        {
            return;
        }

        pollingScreenId = chainId;
        SchedulePendingPoll(screen, PendingPollInterval, chainId);
    }

    private static void SchedulePendingPoll(NCardRewardSelectionScreen screen, double elapsed, ulong chainId)
    {
        SceneTree? tree = screen.GetTree();
        if (tree is null)
        {
            ReleaseChain(chainId);
            return;
        }

        SceneTreeTimer timer = tree.CreateTimer(PendingPollInterval);
        timer.Timeout += () =>
        {
            if (!GodotObject.IsInstanceValid(screen) || !screen.IsInsideTree())
            {
                ReleaseChain(chainId);
                return;
            }

            // Keep re-rendering until every offered card's live result is SETTLED (computed or
            // failed) - not until the global queue is empty. This survives the combat->reward window
            // where the deck isn't readable yet (nothing enqueued), and re-renders re-queue any work
            // that was dropped, so the overlay always converges instead of getting stuck on "...".
            bool settled = RefreshAndCheckSettled(screen);
            if (elapsed < PendingPollCap && !settled)
            {
                SchedulePendingPoll(screen, elapsed + PendingPollInterval, chainId);
            }
            else
            {
                ReleaseChain(chainId); // chain done (settled or capped): allow a fresh chain later
            }
        };
    }

    // Only clears the guard if THIS chain still owns it (a newer screen may have taken over).
    private static void ReleaseChain(ulong chainId)
    {
        if (pollingScreenId == chainId)
        {
            pollingScreenId = 0;
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
        RefreshAndCheckSettled(screen);
    }

    // Renders the reward overlay and returns true iff every live cell shown is settled. Returns
    // false when nothing renderable yet (deck unreadable / cards not ready) so the poll keeps trying.
    private static bool RefreshAndCheckSettled(NCardRewardSelectionScreen screen)
    {
        try
        {
            if (!GodotObject.IsInstanceValid(screen) || !screen.IsInsideTree())
            {
                return true; // screen gone: nothing left to do, stop polling.
            }

            CardOverlayRenderer.BeginSettleTracking();
            CardOverlayRenderer.RenderRewardScreen(screen);
            bool settled = CardOverlayRenderer.EndSettleTracking();
            CardOverlayRenderer.RenderProgressBar(screen, CardOverlayRenderer.PassProgressFraction, CardOverlayRenderer.PassHasPending, CardOverlayRenderer.ProgressTopFractionReward);
            return settled;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"Failed scheduled reward overlay refresh: {ex.Message}", 0);
            return false;
        }
    }
}
