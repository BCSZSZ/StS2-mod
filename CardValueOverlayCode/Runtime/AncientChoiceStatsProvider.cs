using System.Text;
using CardValueOverlay.Core.Ancient;
using GodotFileAccess = Godot.FileAccess;

namespace CardValueOverlay.CardValueOverlayCode.Runtime;

public static class AncientChoiceStatsProvider
{
    private const string ResourcePath = "res://CardValueOverlay/data/ancient_choice_stats.json";

    private static AncientChoiceCatalog? global;
    private static AncientChoiceCatalog? local;
    private static bool loadAttempted;

    public static void Initialize() => EnsureLoaded();

    public static AncientChoiceStatsPair Resolve(string textKey)
    {
        EnsureLoaded();
        string? characterKey = CurrentRunCharacterProvider.TryResolve();
        return new AncientChoiceStatsPair(
            global?.Resolve(textKey, characterKey),
            local?.Resolve(textKey, characterKey));
    }

    internal static void SetLocalCatalog(AncientChoiceCatalog catalog)
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
                MainFile.Logger.Warn($"Ancient choice stats resource not found at {ResourcePath}.", 0);
                return;
            }

            using GodotFileAccess? file = GodotFileAccess.Open(ResourcePath, GodotFileAccess.ModeFlags.Read);
            if (file is null)
            {
                MainFile.Logger.Warn($"Unable to open ancient choice stats resource at {ResourcePath}.", 0);
                return;
            }

            byte[] bytes = file.GetBuffer((long)file.GetLength());
            global = AncientChoiceCatalog.LoadFromJson(Encoding.UTF8.GetString(bytes));
            int choiceScreens = global.Characters.Values.Sum(character => character.TotalChoiceScreens);
            MainFile.Logger.Info(
                $"Loaded ancient choice stats. characters={global.Characters.Count}, "
                + $"choiceScreens={choiceScreens}.",
                0);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"Failed to load ancient choice stats: {ex}", 0);
        }
    }
}

public sealed record AncientChoiceStatsPair(
    AncientChoiceDisplayStats? Global,
    AncientChoiceDisplayStats? Local);
