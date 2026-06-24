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

            CardValueConfig config = CardValueConfigLoader.LoadFromJson(file.GetAsText());
            ConfigValidationResult validation = CardValueConfigLoader.Validate(config);

            foreach (string warning in validation.Warnings)
            {
                MainFile.Logger.Warn($"Config warning: {warning}", 0);
            }

            foreach (string error in validation.Errors)
            {
                MainFile.Logger.Warn($"Config error: {error}", 0);
            }

            if (!validation.IsValid)
            {
                MainFile.Logger.Warn("Config is invalid; using defaults.", 0);
                return CardValueConfig.CreateDefault();
            }

            MainFile.Logger.Info($"Loaded CardValueOverlay config. displayMode={config.Overlay.DisplayMode}.", 0);
            return config;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"Failed to load CardValueOverlay config: {FormatException(ex)}", 0);
            return CardValueConfig.CreateDefault();
        }
    }

    private static string FormatException(Exception ex)
    {
        List<string> messages = [];
        for (Exception? current = ex; current is not null; current = current.InnerException)
        {
            messages.Add($"{current.GetType().FullName}: {current.Message}");
        }

        return string.Join(" -> ", messages);
    }
}
