using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using HandsUp.HandsUpCode.Services;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Runs;

namespace HandsUp.HandsUpCode.Patches;

[HarmonyPatch(typeof(NCombatStartBanner), nameof(NCombatStartBanner.Create))]
public static class SuppressCombatStartBannerDuringPreviousStepReplayRestorePatch
{
    public static bool Prefix(ref NCombatStartBanner? __result)
    {
        if (!SingleplayerPreviousStepRestoreStateService.IsRestoreInProgress)
            return true;

        __result = null;
        return false;
    }
}

[HarmonyPatch(typeof(NEnemyTurnBanner), nameof(NEnemyTurnBanner.Create))]
public static class SuppressEnemyTurnBannerDuringPreviousStepReplayRestorePatch
{
    public static bool Prefix(ref NEnemyTurnBanner? __result)
    {
        if (!SingleplayerPreviousStepRestoreStateService.IsRestoreInProgress)
            return true;

        __result = null;
        return false;
    }
}

[HarmonyPatch(typeof(NPlayerTurnBanner), nameof(NPlayerTurnBanner.Create))]
public static class SuppressPlayerTurnBannerDuringPreviousStepReplayRestorePatch
{
    public static bool Prefix(ref NPlayerTurnBanner? __result)
    {
        if (!SingleplayerPreviousStepRestoreStateService.IsRestoreInProgress)
            return true;

        __result = null;
        return false;
    }
}

[HarmonyPatch(typeof(Cmd), nameof(Cmd.Wait), [typeof(float), typeof(CancellationToken), typeof(bool)])]
public static class SuppressWaitDuringPreviousStepReplayRestorePatch
{
    public static bool Prefix(float seconds, CancellationToken cancelToken, bool ignoreCombatEnd, ref Task __result)
    {
        if (!SingleplayerPreviousStepRestoreStateService.IsRestoreInProgress)
            return true;

        __result = Task.CompletedTask;
        return false;
    }
}

[HarmonyPatch(typeof(Cmd), nameof(Cmd.CustomScaledWait))]
public static class SuppressCustomScaledWaitDuringPreviousStepReplayRestorePatch
{
    public static bool Prefix(
        float fastSeconds,
        float standardSeconds,
        bool ignoreCombatEnd,
        CancellationToken cancellationToken,
        ref Task __result)
    {
        if (!SingleplayerPreviousStepRestoreStateService.IsRestoreInProgress)
            return true;

        __result = Task.CompletedTask;
        return false;
    }
}

[HarmonyPatch(typeof(NetReplayGameService), "get_Type")]
public static class PromotePlayableReplaySessionToSingleplayerPatch
{
    public static bool Prefix(ref NetGameType __result)
    {
        if (!SingleplayerPreviousStepRestoreStateService.IsPlayableReplaySession)
            return true;

        __result = NetGameType.Singleplayer;
        return false;
    }
}

[HarmonyPatch(typeof(RunManager), "get_IsSinglePlayerOrFakeMultiplayer")]
public static class PromotePlayableReplaySessionToSingleplayerRunPatch
{
    public static bool Prefix(ref bool __result)
    {
        if (!SingleplayerPreviousStepRestoreStateService.IsPlayableReplaySession)
            return true;

        __result = true;
        return false;
    }
}
