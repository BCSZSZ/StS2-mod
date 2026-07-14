using BaseLib.Config;
using CardValueOverlay.Core.Configuration;

namespace CardValueOverlay.CardValueOverlayCode.Configuration;

[ConfigHoverTipsByDefault]
internal sealed class CardValueOverlayModConfig : SimpleModConfig
{
    [ConfigSection("RealtimeSimulation")]
    [ConfigSlider(
        RealtimeSimulationSettings.MinimumBranch,
        RealtimeSimulationSettings.MaximumBranch,
        1)]
    public static int SearchBranch { get; set; } = RealtimeSimulationSettings.DefaultBranch;

    [ConfigSlider(
        RealtimeSimulationSettings.MinimumTurnDepth,
        RealtimeSimulationSettings.MaximumTurnDepth,
        1)]
    public static int TurnSearchDepth { get; set; } = RealtimeSimulationSettings.DefaultTurnDepth;

    [ConfigSlider(
        RealtimeSimulationSettings.MinimumAllowedRuns,
        RealtimeSimulationSettings.MaximumAllowedRuns,
        RealtimeSimulationSettings.RunBatchSize)]
    public static int MinimumSimulationRuns { get; set; } = RealtimeSimulationSettings.DefaultMinRuns;

    [ConfigSlider(
        RealtimeSimulationSettings.MinimumAllowedRuns,
        RealtimeSimulationSettings.MaximumAllowedRuns,
        RealtimeSimulationSettings.RunBatchSize)]
    public static int MaximumSimulationRuns { get; set; } = RealtimeSimulationSettings.DefaultMaxRuns;

    [ConfigSlider(
        RealtimeSimulationSettings.MinimumAllowedRuns,
        RealtimeSimulationSettings.MaximumAllowedRuns,
        RealtimeSimulationSettings.RunBatchSize)]
    public static int ComplexCardMinimumRuns { get; set; } = RealtimeSimulationSettings.DefaultComplexCardMinRuns;

    [ConfigSlider(
        RealtimeSimulationSettings.MinimumConfidenceLevelPercent,
        RealtimeSimulationSettings.MaximumConfidenceLevelPercent,
        1)]
    public static int ConfidenceLevelPercent { get; set; } = RealtimeSimulationSettings.DefaultConfidenceLevelPercent;

    public static bool EarlyStoppingEnabled { get; set; } = RealtimeSimulationSettings.DefaultEarlyStoppingEnabled;

    public static RealtimeSimulationSettings CurrentSettings =>
        RealtimeSimulationSettings.Normalize(
            SearchBranch,
            TurnSearchDepth,
            MinimumSimulationRuns,
            MaximumSimulationRuns,
            ComplexCardMinimumRuns,
            ConfidenceLevelPercent,
            EarlyStoppingEnabled);
}
