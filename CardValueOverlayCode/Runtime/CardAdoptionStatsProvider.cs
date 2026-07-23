using System.Text;
using CardValueOverlay.Core.Adoption;
using CardValueOverlay.Core.Configuration;
using GodotFileAccess = Godot.FileAccess;

namespace CardValueOverlay.CardValueOverlayCode.Runtime;

public static class CardAdoptionStatsProvider
{
    private const string ResourcePath = "res://CardValueOverlay/data/card_adoption.json";

    private static CardAdoptionCatalog? global;
    private static CardAdoptionCatalog? local;
    private static bool loadAttempted;

    public static void Initialize() => EnsureLoaded();

    public static CardAdoptionStatsPair Resolve(string cardKey, CardUpgradeState upgradeState)
    {
        EnsureLoaded();
        string? characterKey = CurrentRunCharacterProvider.TryResolve();
        return new CardAdoptionStatsPair(
            global?.Resolve(cardKey, upgradeState, characterKey),
            local?.Resolve(cardKey, upgradeState, characterKey));
    }

    internal static CardAdoptionCatalog? ReferenceCatalog
    {
        get
        {
            EnsureLoaded();
            return global;
        }
    }

    internal static void SetLocalCatalog(CardAdoptionCatalog catalog)
    {
        local = catalog;
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
            global = CardAdoptionCatalog.LoadFromJson(Encoding.UTF8.GetString(bytes));
            MainFile.Logger.Info(
                $"Loaded card adoption stats. totalRuns={global.TotalRuns}, cards={global.Cards.Count}.",
                0);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"Failed to load card adoption stats: {ex}", 0);
        }
    }
}

public sealed record CardAdoptionStatsPair(
    CardAdoptionDisplayStats? Global,
    CardAdoptionDisplayStats? Local);
