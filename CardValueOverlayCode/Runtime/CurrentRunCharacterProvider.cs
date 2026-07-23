namespace CardValueOverlay.CardValueOverlayCode.Runtime;

internal static class CurrentRunCharacterProvider
{
    private static bool readFailureLogged;

    public static string? TryResolve()
    {
        try
        {
            MegaCrit.Sts2.Core.Runs.RunManager? manager = MegaCrit.Sts2.Core.Runs.RunManager.Instance;
            if (manager is null || !manager.IsInProgress)
            {
                return null;
            }

            MegaCrit.Sts2.Core.Runs.RunState? state = manager.DebugOnlyGetState();
            if (state is null || state.Players.Count == 0)
            {
                return null;
            }

            return state.Players[0].Character.Id.ToString();
        }
        catch (Exception ex)
        {
            if (!readFailureLogged)
            {
                readFailureLogged = true;
                MainFile.Logger.Warn($"Failed to read the current run character: {ex}", 0);
            }
            return null;
        }
    }
}
