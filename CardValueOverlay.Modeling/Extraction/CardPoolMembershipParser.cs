using System.Text.RegularExpressions;

namespace CardValueOverlay.Modeling.Extraction;

public sealed class CardPoolMembershipParser
{
    private static readonly Regex ClassNamePattern = new(
        @"class\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*:\s*CardPoolModel",
        RegexOptions.Compiled);

    private static readonly Regex TitlePattern = new(
        @"Title\s*=>\s*""(?<title>[^""]+)""",
        RegexOptions.Compiled);

    private static readonly Regex CardPattern = new(
        @"ModelDb\.Card<(?<type>[A-Za-z_][A-Za-z0-9_]*)>\s*\(",
        RegexOptions.Compiled);

    private static readonly Regex MultiplayerConstraintPattern = new(
        @"MultiplayerConstraint\s*=>\s*CardMultiplayerConstraint\.(?<constraint>[A-Za-z_][A-Za-z0-9_]*)",
        RegexOptions.Compiled);

    public CardPoolSourceEntry ParsePoolSource(string fallbackPoolTypeName, string source)
    {
        string poolTypeName = ClassNamePattern.Match(source).Groups["name"].Value;
        if (string.IsNullOrWhiteSpace(poolTypeName))
        {
            poolTypeName = fallbackPoolTypeName;
        }

        string poolName = NormalizePoolName(poolTypeName, TitlePattern.Match(source).Groups["title"].Value);
        IReadOnlyList<string> cards = CardPattern
            .Matches(source)
            .Select(match => match.Groups["type"].Value)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        return new CardPoolSourceEntry(poolName, poolTypeName, cards);
    }

    public string ParseMultiplayerConstraint(string source)
    {
        Match match = MultiplayerConstraintPattern.Match(source);
        return match.Success ? match.Groups["constraint"].Value : "None";
    }

    private static string NormalizePoolName(string poolTypeName, string title)
    {
        string name = poolTypeName.EndsWith("CardPool", StringComparison.Ordinal)
            ? poolTypeName[..^"CardPool".Length]
            : poolTypeName;

        if (string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(title))
        {
            name = title;
        }

        return name switch
        {
            "Ironclad" or "ironclad" => "Ironclad",
            "Silent" or "silent" => "Silent",
            "Defect" or "defect" => "Defect",
            "Necrobinder" or "necrobinder" => "Necrobinder",
            "Regent" or "regent" => "Regent",
            "Colorless" or "colorless" => "Colorless",
            "Event" or "event" => "Event",
            "Curse" or "curse" => "Curse",
            "Status" or "status" => "Status",
            "Token" or "token" => "Token",
            "Quest" or "quest" => "Quest",
            "Deprecated" or "deprecated" => "Deprecated",
            "Mock" or "mock" => "Mock",
            _ => name
        };
    }
}
