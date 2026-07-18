namespace CardValueOverlay.Core.Configuration;

/// <summary>
/// Validated selective Branch 3 policy for realtime overlay simulations. The third candidate is
/// eligible for removal only when it is not an engine/resource/card-object card and trails the
/// second candidate by at least this many damage-equivalent score points.
/// </summary>
public static class RealtimeSearchBranchPolicy
{
    public const int SelectiveThirdBranchMinScoreGap = 13;
}
