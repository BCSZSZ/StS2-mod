using MegaCrit.Sts2.Core.Localization;

namespace CardValueOverlay.CardValueOverlayCode.Runtime;

public static class LocalizedText
{
    public static string? Resolve(string? table, string? key, string? fallback)
    {
        if (string.IsNullOrWhiteSpace(table) || string.IsNullOrWhiteSpace(key))
        {
            return fallback;
        }

        try
        {
            if (!LocString.Exists(table, key))
            {
                return fallback;
            }

            string text = new LocString(table, key).GetFormattedText();
            return string.IsNullOrWhiteSpace(text) ? fallback : text;
        }
        catch
        {
            return fallback;
        }
    }
}
