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
        RealtimeSimulationSettings.MinimumRuns,
        RealtimeSimulationSettings.MaximumRuns,
        1)]
    public static int SimulationRuns { get; set; } = RealtimeSimulationSettings.DefaultRuns;

    public static RealtimeSimulationSettings CurrentSettings =>
        RealtimeSimulationSettings.Normalize(SearchBranch, TurnSearchDepth, SimulationRuns);
}
