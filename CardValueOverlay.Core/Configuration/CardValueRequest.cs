namespace CardValueOverlay.Core.Configuration;

public sealed record CardValueRequest(string CardKey, CardUpgradeState UpgradeState)
{
    public string DisplayKey => UpgradeState == CardUpgradeState.Upgraded ? $"{CardKey}+" : CardKey;

    public static CardValueRequest Parse(string raw)
    {
        string value = raw.Trim();
        if (value.EndsWith('+'))
        {
            return new CardValueRequest(value[..^1].Trim(), CardUpgradeState.Upgraded);
        }

        foreach ((string suffix, CardUpgradeState state) in StateSuffixes)
        {
            if (value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return new CardValueRequest(value[..^suffix.Length].Trim(), state);
            }
        }

        return new CardValueRequest(value, CardUpgradeState.Unupgraded);
    }

    public static string StateKey(CardUpgradeState state)
    {
        return state switch
        {
            CardUpgradeState.Unupgraded => "unupgraded",
            CardUpgradeState.Upgraded => "upgraded",
            _ => state.ToString()
        };
    }

    private static readonly (string Suffix, CardUpgradeState State)[] StateSuffixes =
    [
        (":unupgraded", CardUpgradeState.Unupgraded),
        ("|unupgraded", CardUpgradeState.Unupgraded),
        ("@unupgraded", CardUpgradeState.Unupgraded),
        (":base", CardUpgradeState.Unupgraded),
        ("|base", CardUpgradeState.Unupgraded),
        ("@base", CardUpgradeState.Unupgraded),
        (":upgraded", CardUpgradeState.Upgraded),
        ("|upgraded", CardUpgradeState.Upgraded),
        ("@upgraded", CardUpgradeState.Upgraded),
        (":up", CardUpgradeState.Upgraded),
        ("|up", CardUpgradeState.Upgraded),
        ("@up", CardUpgradeState.Upgraded)
    ];
}
