using System.Threading.Tasks;
using HarmonyLib;
using HandsUp.HandsUpCode.Services;
using MegaCrit.Sts2.Core.Runs;

namespace HandsUp.HandsUpCode.Patches;

[HarmonyPatch(typeof(RunManager), "FadeIn", [typeof(bool)])]
public static class SuppressRunManagerFadeInDuringMultiplayerPreviousStepRestorePatch
{
    public static bool Prefix(ref Task __result)
    {
        if (!MultiplayerPreviousStepRestoreStateService.IsRestoreInProgress)
            return true;

        __result = Task.CompletedTask;
        return false;
    }
}
