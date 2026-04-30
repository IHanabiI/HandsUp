using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using HandsUp.HandsUpCode.Services;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace HandsUp.HandsUpCode.Patches;

[HarmonyPatch(typeof(NCombatStartBanner), nameof(NCombatStartBanner.Create))]
public static class SuppressMultiplayerCombatStartBannerDuringPreviousStepRestorePatch
{
    public static bool Prefix(ref NCombatStartBanner? __result)
    {
        if (!MultiplayerPreviousStepRestoreStateService.IsRestoreInProgress)
            return true;

        __result = null;
        return false;
    }
}

[HarmonyPatch(typeof(NEnemyTurnBanner), nameof(NEnemyTurnBanner.Create))]
public static class SuppressMultiplayerEnemyTurnBannerDuringPreviousStepRestorePatch
{
    public static bool Prefix(ref NEnemyTurnBanner? __result)
    {
        if (!MultiplayerPreviousStepRestoreStateService.IsRestoreInProgress)
            return true;

        __result = null;
        return false;
    }
}

[HarmonyPatch(typeof(NPlayerTurnBanner), nameof(NPlayerTurnBanner.Create))]
public static class SuppressMultiplayerPlayerTurnBannerDuringPreviousStepRestorePatch
{
    public static bool Prefix(ref NPlayerTurnBanner? __result)
    {
        if (!MultiplayerPreviousStepRestoreStateService.IsRestoreInProgress)
            return true;

        __result = null;
        return false;
    }
}

[HarmonyPatch(typeof(Cmd), nameof(Cmd.Wait), [typeof(float), typeof(CancellationToken), typeof(bool)])]
public static class SuppressMultiplayerWaitDuringPreviousStepRestorePatch
{
    public static bool Prefix(float seconds, CancellationToken cancelToken, bool ignoreCombatEnd, ref Task __result)
    {
        if (!MultiplayerPreviousStepRestoreStateService.IsRestoreInProgress)
            return true;

        __result = Task.CompletedTask;
        return false;
    }
}

[HarmonyPatch(typeof(Cmd), nameof(Cmd.CustomScaledWait))]
public static class SuppressMultiplayerCustomScaledWaitDuringPreviousStepRestorePatch
{
    public static bool Prefix(
        float fastSeconds,
        float standardSeconds,
        bool ignoreCombatEnd,
        CancellationToken cancellationToken,
        ref Task __result)
    {
        if (!MultiplayerPreviousStepRestoreStateService.IsRestoreInProgress)
            return true;

        __result = Task.CompletedTask;
        return false;
    }
}
