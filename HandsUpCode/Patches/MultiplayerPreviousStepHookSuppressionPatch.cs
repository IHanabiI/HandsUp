using System.Threading.Tasks;
using HarmonyLib;
using HandsUp.HandsUpCode.Services;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Runs;

namespace HandsUp.HandsUpCode.Patches;

[HarmonyPatch(typeof(Hook), nameof(Hook.BeforeCombatStart))]
public static class SuppressMultiplayerBeforeCombatStartHooksDuringPreviousStepRestorePatch
{
    public static bool Prefix(ref Task __result)
    {
        if (!MultiplayerPreviousStepRestoreStateService.ShouldSuppressCombatStartHooks)
            return true;

        __result = Task.CompletedTask;
        return false;
    }
}

[HarmonyPatch(typeof(Hook), nameof(Hook.BeforeHandDraw))]
public static class SuppressMultiplayerBeforeHandDrawHooksDuringPreviousStepRestorePatch
{
    public static bool Prefix(CombatState combatState, Player player, PlayerChoiceContext playerChoiceContext, ref Task __result)
    {
        if (!MultiplayerPreviousStepRestoreStateService.ShouldSuppressCombatStartHooks)
            return true;

        __result = Task.CompletedTask;
        return false;
    }
}

[HarmonyPatch(typeof(Hook), nameof(Hook.AfterPlayerTurnStart))]
public static class SuppressMultiplayerAfterPlayerTurnStartHooksDuringPreviousStepRestorePatch
{
    public static bool Prefix(CombatState combatState, PlayerChoiceContext choiceContext, Player player, ref Task __result)
    {
        if (!MultiplayerPreviousStepRestoreStateService.ShouldSuppressCombatStartHooks)
            return true;

        __result = Task.CompletedTask;
        return false;
    }
}

[HarmonyPatch(typeof(Hook), nameof(Hook.BeforePlayPhaseStart))]
public static class SuppressMultiplayerBeforePlayPhaseStartHooksDuringPreviousStepRestorePatch
{
    public static bool Prefix(CombatState combatState, Player player, ref Task __result)
    {
        if (!MultiplayerPreviousStepRestoreStateService.ShouldSuppressCombatStartHooks)
            return true;

        __result = Task.CompletedTask;
        return false;
    }
}
