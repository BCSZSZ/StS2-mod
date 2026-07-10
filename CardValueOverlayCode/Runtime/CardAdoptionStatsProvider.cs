using System.Text;
using CardValueOverlay.Core.Adoption;
using CardValueOverlay.Core.Configuration;
using GodotFileAccess = Godot.FileAccess;

namespace CardValueOverlay.CardValueOverlayCode.Runtime;

public static class CardAdoptionStatsProvider
{
    private const string ResourcePath = "res://CardValueOverlay/data/card_adoption.json";

    private static CardAdoptionCatalog? current;
    private static bool loadAttempted;

    public static CardAdoptionDisplayStats? Resolve(string cardKey, CardUpgradeState upgradeState)
    {
        EnsureLoaded();
        return current?.Resolve(cardKey, upgradeState);
    }

    private static void EnsureLoaded()
    {
        if (loadAttempted)
        {
            return;
        }

        loadAttempted = true;
        try
        {
            if (!GodotFileAccess.FileExists(ResourcePath))
            {
                MainFile.Logger.Warn($"Card adoption resource not found at {ResourcePath}.", 0);
                return;
            }

            using GodotFileAccess? file = GodotFileAccess.Open(ResourcePath, GodotFileAccess.ModeFlags.Read);
            if (file is null)
            {
                MainFile.Logger.Warn($"Unable to open card adoption resource at {ResourcePath}.", 0);
                return;
            }

            byte[] bytes = file.GetBuffer((long)file.GetLength());
            current = CardAdoptionCatalog.LoadFromJson(Encoding.UTF8.GetString(bytes));
            MainFile.Logger.Info(
                $"Loaded card adoption stats. totalRuns={current.TotalRuns}, cards={current.Cards.Count}.",
                0);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"Failed to load card adoption stats: {ex}", 0);
        }
    }
}
