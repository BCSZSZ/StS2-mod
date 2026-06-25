using CardValueOverlay.Modeling.Estimation;
using CardValueOverlay.Modeling.Extraction;

namespace CardValueOverlay.Modeling.Export;

public sealed class CardValueReviewReportWriter
{
    private const string NoContributionWarning = "No supported contribution was estimated for this card.";

    private static readonly string[] GroupOrder =
    [
        "Ironclad / 战士",
        "Silent",
        "Defect",
        "Necrobinder",
        "Regent",
        "Colorless / 无色",
        "Event",
        "Ancient rarity",
        "Curse / Status / Token / Quest",
        "Other"
    ];

    public void Write(
        string path,
        IReadOnlyList<CardValueEstimate> estimates,
        IReadOnlyList<CardPoolMembershipEntry> memberships)
    {
        Dictionary<string, CardPoolMembershipEntry> membershipById = memberships.ToDictionary(
            item => item.ModelId,
            StringComparer.Ordinal);

        IReadOnlyList<CardReviewRow> rows = estimates
            .Select(estimate => MakeRow(estimate, membershipById.TryGetValue(estimate.ModelId, out CardPoolMembershipEntry? membership) ? membership : null))
            .ToArray();
        IReadOnlyList<CardReviewRow> reviewRows = rows
            .Where(row => row.Warnings.Count > 0 || row.IsUnscored || row.IsZeroValue)
            .ToArray();

        using StreamWriter writer = new(path);
        writer.WriteLine("# V1 Card Value Review List");
        writer.WriteLine();
        writer.WriteLine("Localization fields are reserved for later extraction and intentionally left blank for now.");
        writer.WriteLine();
        writer.WriteLine("## Coverage");
        writer.WriteLine();
        writer.WriteLine("| Metric | Count |");
        writer.WriteLine("| --- | ---: |");
        writer.WriteLine($"| Value candidate rows | {estimates.Count} |");
        writer.WriteLine($"| Card pool membership rows | {memberships.Count} |");
        writer.WriteLine($"| Cards requiring review | {reviewRows.Count} |");
        writer.WriteLine($"| Unscored cards | {rows.Count(row => row.IsUnscored)} |");
        writer.WriteLine($"| Scored cards with warnings | {rows.Count(row => !row.IsUnscored && row.Warnings.Count > 0)} |");
        writer.WriteLine($"| Scored zero-value cards | {rows.Count(row => !row.IsUnscored && row.IsZeroValue)} |");
        writer.WriteLine($"| Multiplayer-only cards | {rows.Count(row => row.MultiplayerConstraint == "MultiplayerOnly")} |");
        writer.WriteLine($"| Singleplayer-only cards | {rows.Count(row => row.MultiplayerConstraint == "SingleplayerOnly")} |");

        WriteGroupSummary(writer, reviewRows);
        foreach (string group in GroupOrder)
        {
            IReadOnlyList<CardReviewRow> groupRows = reviewRows
                .Where(row => string.Equals(row.ReviewGroup, group, StringComparison.Ordinal))
                .OrderBy(row => row.TypeName, StringComparer.Ordinal)
                .ToArray();
            WriteGroup(writer, group, groupRows);
        }

        WriteWarningIndex(writer, rows);
    }

    private static CardReviewRow MakeRow(CardValueEstimate estimate, CardPoolMembershipEntry? membership)
    {
        IReadOnlyList<string> pools = membership?.Pools ?? [];
        IReadOnlyList<string> warnings = estimate.Warnings
            .Concat(membership?.Warnings ?? [])
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        string multiplayerConstraint = membership?.MultiplayerConstraint ?? "Unknown";
        bool isUnscored = warnings.Contains(NoContributionWarning, StringComparer.Ordinal) || estimate.Contributions.Count == 0;
        bool isZeroValue = estimate.EstimatedValue == 0m;

        return new CardReviewRow(
            estimate.TypeName,
            null,
            null,
            estimate.Cost,
            estimate.CardType,
            estimate.Rarity,
            estimate.TargetType,
            estimate.EstimatedValue,
            estimate.UpgradedEstimatedValue,
            estimate.SmithValue,
            estimate.Confidence,
            pools,
            ResolveReviewGroup(estimate, pools),
            multiplayerConstraint,
            isUnscored,
            isZeroValue,
            warnings);
    }

    private static string ResolveReviewGroup(CardValueEstimate estimate, IReadOnlyList<string> pools)
    {
        if (string.Equals(estimate.Rarity, "Ancient", StringComparison.Ordinal))
        {
            return "Ancient rarity";
        }

        if (pools.Contains("Ironclad", StringComparer.Ordinal))
        {
            return "Ironclad / 战士";
        }

        if (pools.Contains("Silent", StringComparer.Ordinal))
        {
            return "Silent";
        }

        if (pools.Contains("Defect", StringComparer.Ordinal))
        {
            return "Defect";
        }

        if (pools.Contains("Necrobinder", StringComparer.Ordinal))
        {
            return "Necrobinder";
        }

        if (pools.Contains("Regent", StringComparer.Ordinal))
        {
            return "Regent";
        }

        if (pools.Contains("Colorless", StringComparer.Ordinal))
        {
            return "Colorless / 无色";
        }

        if (pools.Contains("Event", StringComparer.Ordinal))
        {
            return "Event";
        }

        if (pools.Any(IsSpecialPool)
            || string.Equals(estimate.CardType, "Curse", StringComparison.Ordinal)
            || string.Equals(estimate.CardType, "Status", StringComparison.Ordinal)
            || string.Equals(estimate.CardType, "Quest", StringComparison.Ordinal)
            || string.Equals(estimate.CardType, "Token", StringComparison.Ordinal)
            || string.Equals(estimate.Rarity, "Curse", StringComparison.Ordinal)
            || string.Equals(estimate.Rarity, "Status", StringComparison.Ordinal)
            || string.Equals(estimate.Rarity, "Quest", StringComparison.Ordinal)
            || string.Equals(estimate.Rarity, "Token", StringComparison.Ordinal))
        {
            return "Curse / Status / Token / Quest";
        }

        return "Other";
    }

    private static bool IsSpecialPool(string pool)
    {
        return pool is "Curse" or "Status" or "Token" or "Quest";
    }

    private static void WriteGroupSummary(StreamWriter writer, IReadOnlyList<CardReviewRow> reviewRows)
    {
        writer.WriteLine();
        writer.WriteLine("## Review Group Summary");
        writer.WriteLine();
        writer.WriteLine("| Group | Review cards | Unscored | Scored warnings | Zero-value scored | Multiplayer-only | Singleplayer-only |");
        writer.WriteLine("| --- | ---: | ---: | ---: | ---: | ---: | ---: |");
        foreach (string group in GroupOrder)
        {
            IReadOnlyList<CardReviewRow> rows = reviewRows.Where(row => row.ReviewGroup == group).ToArray();
            writer.WriteLine(
                $"| {Escape(group)} | {rows.Count} | {rows.Count(row => row.IsUnscored)} | {rows.Count(row => !row.IsUnscored && row.Warnings.Count > 0)} | {rows.Count(row => !row.IsUnscored && row.IsZeroValue)} | {rows.Count(row => row.MultiplayerConstraint == "MultiplayerOnly")} | {rows.Count(row => row.MultiplayerConstraint == "SingleplayerOnly")} |");
        }
    }

    private static void WriteGroup(StreamWriter writer, string group, IReadOnlyList<CardReviewRow> rows)
    {
        writer.WriteLine();
        writer.WriteLine($"## {group} ({rows.Count})");
        writer.WriteLine();
        if (rows.Count == 0)
        {
            writer.WriteLine("None.");
            return;
        }

        WriteSubgroup(writer, "Unscored", rows.Where(row => row.IsUnscored).ToArray());
        WriteSubgroup(writer, "Scored with warnings", rows.Where(row => !row.IsUnscored && row.Warnings.Count > 0).ToArray());
        WriteSubgroup(writer, "Scored zero-value without warnings", rows.Where(row => !row.IsUnscored && row.IsZeroValue && row.Warnings.Count == 0).ToArray());
    }

    private static void WriteSubgroup(StreamWriter writer, string title, IReadOnlyList<CardReviewRow> rows)
    {
        writer.WriteLine();
        writer.WriteLine($"### {title} ({rows.Count})");
        writer.WriteLine();
        if (rows.Count == 0)
        {
            writer.WriteLine("None.");
            return;
        }

        writer.WriteLine("| Card | Localized name | Description | Pools | Multiplayer | Cost | Type | Rarity | Target | Value | Upgraded | Smith | Confidence | Warnings |");
        writer.WriteLine("| --- | --- | --- | --- | --- | ---: | --- | --- | --- | ---: | ---: | ---: | ---: | --- |");
        foreach (CardReviewRow row in rows.OrderBy(row => row.TypeName, StringComparer.Ordinal))
        {
            writer.WriteLine(
                $"| {Escape(row.TypeName)} | {Escape(row.LocalizedName ?? "")} | {Escape(row.Description ?? "")} | {Escape(string.Join(", ", row.Pools))} | {Escape(row.MultiplayerConstraint)} | {row.Cost?.ToString() ?? ""} | {Escape(row.CardType ?? "")} | {Escape(row.Rarity ?? "")} | {Escape(row.TargetType ?? "")} | {row.EstimatedValue:0.###} | {row.UpgradedEstimatedValue:0.###} | {row.SmithValue:0.###} | {row.Confidence:0.###} | {Escape(string.Join("<br>", row.Warnings))} |");
        }
    }

    private static void WriteWarningIndex(StreamWriter writer, IReadOnlyList<CardReviewRow> rows)
    {
        writer.WriteLine();
        writer.WriteLine("## Warning Reason Index");
        writer.WriteLine();
        IReadOnlyList<IGrouping<string, CardReviewRow>> warningGroups = rows
            .SelectMany(row => row.Warnings.Select(warning => (warning, row)))
            .GroupBy(item => item.warning, item => item.row, StringComparer.Ordinal)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .ToArray();

        foreach (IGrouping<string, CardReviewRow> group in warningGroups)
        {
            writer.WriteLine();
            writer.WriteLine($"### {Escape(group.Key)} ({group.Count()})");
            writer.WriteLine();
            writer.WriteLine("| Card | Group | Pools | Multiplayer | Cost | Type | Rarity | Value |");
            writer.WriteLine("| --- | --- | --- | --- | ---: | --- | --- | ---: |");
            foreach (CardReviewRow row in group.OrderBy(row => row.ReviewGroup, StringComparer.Ordinal).ThenBy(row => row.TypeName, StringComparer.Ordinal))
            {
                writer.WriteLine(
                    $"| {Escape(row.TypeName)} | {Escape(row.ReviewGroup)} | {Escape(string.Join(", ", row.Pools))} | {Escape(row.MultiplayerConstraint)} | {row.Cost?.ToString() ?? ""} | {Escape(row.CardType ?? "")} | {Escape(row.Rarity ?? "")} | {row.EstimatedValue:0.###} |");
            }
        }
    }

    private static string Escape(string value)
    {
        return value.Replace("|", "\\|", StringComparison.Ordinal);
    }

    private sealed record CardReviewRow(
        string TypeName,
        string? LocalizedName,
        string? Description,
        int? Cost,
        string? CardType,
        string? Rarity,
        string? TargetType,
        decimal EstimatedValue,
        decimal UpgradedEstimatedValue,
        decimal SmithValue,
        double Confidence,
        IReadOnlyList<string> Pools,
        string ReviewGroup,
        string MultiplayerConstraint,
        bool IsUnscored,
        bool IsZeroValue,
        IReadOnlyList<string> Warnings);
}
