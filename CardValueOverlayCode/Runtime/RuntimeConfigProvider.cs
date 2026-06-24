using CardValueOverlay.Core.Configuration;
using Godot;
using GodotFileAccess = Godot.FileAccess;

namespace CardValueOverlay.CardValueOverlayCode.Runtime;

public static class RuntimeConfigProvider
{
    private const string ConfigResourcePath = "res://CardValueOverlay/data/card_values.json";

    private static CardValueConfig? current;

    public static CardValueConfig Current => current ??= Load();

    public static void Reload()
    {
        current = Load();
    }

    private static CardValueConfig Load()
    {
        try
        {
            if (!GodotFileAccess.FileExists(ConfigResourcePath))
            {
                MainFile.Logger.Warn($"Config resource not found at {ConfigResourcePath}; using defaults.", 0);
                return CardValueConfig.CreateDefault();
            }

            using GodotFileAccess? file = GodotFileAccess.Open(ConfigResourcePath, GodotFileAccess.ModeFlags.Read);
            if (file is null)
            {
                MainFile.Logger.Warn($"Unable to open config resource at {ConfigResourcePath}; using defaults.", 0);
                return CardValueConfig.CreateDefault();
            }

            return CardValueConfigLoader.LoadFromJson(file.GetAsText());
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"Failed to load CardValueOverlay config: {ex.Message}", 0);
            return CardValueConfig.CreateDefault();
        }
    }
}
