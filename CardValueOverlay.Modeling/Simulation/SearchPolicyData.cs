using System.Text.Json;
using System.Text.Json.Serialization;

namespace CardValueOverlay.Modeling.Simulation;

public sealed record SearchPolicyDecisionGroup(
    int SchemaVersion,
    string GroupId,
    string Source,
    int Run,
    int Turn,
    int ActionsPlayed,
    IReadOnlyDictionary<string, double> ContextFeatures,
    IReadOnlyList<SearchPolicyActionSample> Actions,
    int TeacherBestActionIndex,
    SearchPolicyGroupMetadata Metadata)
{
    public const int CurrentSchemaVersion = 1;
}

public sealed record SearchPolicyActionSample(
    string CardModelId,
    string CardTypeName,
    int InstanceId,
    IReadOnlyDictionary<string, double> Features,
    double HeuristicScore,
    double TeacherRouteValue,
    int TeacherRank);

public sealed record SearchPolicyGroupMetadata(
    string RunId,
    int DeckIndex,
    string Variant,
    int TeacherMaxBranchingCards,
    int TeacherMaxCardsPlayedPerTurn);

public sealed class SearchPolicyDataCollector
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly object sync = new();
    private readonly TextWriter writer;
    private int count;
    private int activeMaxDecisionGroups;

    public SearchPolicyDataCollector(
        TextWriter writer,
        int maxDecisionGroups)
    {
        this.writer = writer;
        MaxDecisionGroups = Math.Max(0, maxDecisionGroups);
        activeMaxDecisionGroups = MaxDecisionGroups;
    }

    public int Count
    {
        get
        {
            lock (sync)
            {
                return count;
            }
        }
    }

    public int MaxDecisionGroups { get; }

    public void SetActiveLimit(int maxDecisionGroups)
    {
        lock (sync)
        {
            activeMaxDecisionGroups = Math.Clamp(maxDecisionGroups, 0, MaxDecisionGroups);
        }
    }

    public bool CanCollect
    {
        get
        {
            lock (sync)
            {
                return count < activeMaxDecisionGroups;
            }
        }
    }

    public bool TryAdd(SearchPolicyDecisionGroup group)
    {
        lock (sync)
        {
            if (count >= activeMaxDecisionGroups)
            {
                return false;
            }

            SearchPolicyDecisionGroup numbered = group with
            {
                GroupId = $"{group.Source}:{group.Metadata.DeckIndex}:{group.Metadata.Variant}:{count}"
            };
            writer.WriteLine(JsonSerializer.Serialize(numbered, JsonOptions));
            count++;
            return true;
        }
    }
}
