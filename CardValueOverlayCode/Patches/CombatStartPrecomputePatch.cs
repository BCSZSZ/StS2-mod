using CardValueOverlay.CardValueOverlayCode.Runtime;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;

namespace CardValueOverlay.CardValueOverlayCode.Patches;

// Pause the background EV worker while a combat is active so heavy sims never stutter the fight.
// Computation happens at the reward/deck/upgrade screens instead. Any unfinished reward-screen work
// waits - and work for a deck/floor you've already moved past is discarded - until the next screen.
[HarmonyPatch(typeof(CombatManager), nameof(CombatManager.SetUpCombat))]
public static class CombatStartPatch
{
    public static void Postfix()
    {
        try
        {
            RealtimeEvService.OnCombatStart();
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"OnCombatStart failed: {ex.Message}", 0);
        }
    }
}

[HarmonyPatch(typeof(CombatManager), nameof(CombatManager.Reset))]
public static class CombatEndPatch
{
    public static void Postfix()
    {
        try
        {
            RealtimeEvService.OnCombatEnd();
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"OnCombatEnd failed: {ex.Message}", 0);
        }
    }
}
