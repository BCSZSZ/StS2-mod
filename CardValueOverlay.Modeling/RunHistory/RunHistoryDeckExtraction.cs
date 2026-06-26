namespace CardValueOverlay.Modeling.RunHistory;

public sealed record RunHistoryDeckExtractionOptions
{
    public string? HistoryRoot { get; init; }

    public string CatalogPath { get; init; } = "data/extracted/card_catalog.generated.json";

    public string Character { get; init; } = "CHARACTER.REGENT";

    public int Ascension { get; init; } = 10;

    public int Floor { get; init; } = 5;

    public int Limit { get; init; } = 5;

    public string? RunId { get; init; }

    public bool IncludeFloorRewards { get; init; } = true;
}

public sealed record RunHistoryDeckExtractionReport
{
    public DateTimeOffset GeneratedAt { get; init; }

    public string HistoryRoot { get; init; } = "";

    public string CatalogPath { get; init; } = "";

    public string Character { get; init; } = "CHARACTER.REGENT";

    public int Ascension { get; init; } = 10;

    public int Floor { get; init; } = 5;

    public bool IncludesFloorRewards { get; init; }

    public IReadOnlyList<RunHistoryDeckResult> Runs { get; init; } = [];
}

public sealed record RunHistoryDeckResult
{
    public string RunId { get; init; } = "";

    public long StartTime { get; init; }

    public string Build { get; init; } = "";

    public string Seed { get; init; } = "";

    public string Path { get; init; } = "";

    public string Character { get; init; } = "CHARACTER.REGENT";

    public int Ascension { get; init; } = 10;

    public int Floor { get; init; } = 5;

    public bool IncludesFloorRewards { get; init; }

    public int DeckCount { get; init; }

    public IReadOnlyList<string> Events { get; init; } = [];

    public IReadOnlyList<RunHistoryDeckCard> Cards { get; init; } = [];
}

public sealed record RunHistoryDeckCard
{
    public int Count { get; init; }

    public string Id { get; init; } = "";

    public string TypeName { get; init; } = "";

    public int Upgrade { get; init; }
}
