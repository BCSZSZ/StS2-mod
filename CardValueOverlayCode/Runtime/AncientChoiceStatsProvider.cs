using System.Text;
using CardValueOverlay.Core.Ancient;
using GodotFileAccess = Godot.FileAccess;

namespace CardValueOverlay.CardValueOverlayCode.Runtime;

public static class AncientChoiceStatsProvider
{
    private const string ResourcePath = "res://CardValueOverlay/data/ancient_choice_stats.json";

    private static AncientChoiceCatalog? current;
    private static bool loadAttempted;

    public static AncientChoiceDisplayStats? Resolve(string textKey)
    {
        EnsureLoaded();
        return current?.Resolve(textKey);
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
            current = AncientChoiceCatalog.LoadFromJson(Encoding.UTF8.GetString(bytes));
            MainFile.Logger.Info(
                $"Loaded ancient choice stats. choiceScreens={current.TotalChoiceScreens}, choices={current.Choices.Count}.",
                0);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"Failed to load ancient choice stats: {ex}", 0);
        }
    }
}
