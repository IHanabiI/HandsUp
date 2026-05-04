using System.Threading.Tasks;
using HarmonyLib;
using HandsUp.HandsUpCode.Services;
using MegaCrit.Sts2.Core.Hooks;

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
    public static bool Prefix(ref Task __result)
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
    public static bool Prefix(ref Task __result)
    {
        if (!MultiplayerPreviousStepRestoreStateService.ShouldSuppressCombatStartHooks)
            return true;

        __result = Task.CompletedTask;
        return false;
    }
}
