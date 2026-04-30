using HarmonyLib;
using HandsUp.HandsUpCode.Services;
using MegaCrit.Sts2.Core.Nodes;

namespace HandsUp.HandsUpCode.Patches;

[HarmonyPatch(typeof(NRun), nameof(NRun._Notification))]
public static class MultiplayerReloadDetachedRunCleanupPatch
{
    private const int NotificationPredelete = 1006;

    public static bool Prefix(NRun __instance, int what)
    {
        if (what != NotificationPredelete)
            return true;

        if (!MultiplayerReloadSceneDetachGuardService.ShouldSuppressCleanup(__instance))
            return true;

        MainFile.Logger.Info(
            $"Suppressed detached old NRun cleanup during multiplayer reload for scene {__instance.GetInstanceId()}.");
        return false;
    }
}
