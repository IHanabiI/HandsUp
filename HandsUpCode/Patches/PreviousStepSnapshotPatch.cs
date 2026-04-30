using System.Threading.Tasks;
using HarmonyLib;
using HandsUp.HandsUpCode.Services;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Runs;

namespace HandsUp.HandsUpCode.Patches;

[HarmonyPatch(typeof(CombatManager), "StartTurn")]
public static class CapturePreviousStepSnapshotPatch
{
    public static async void Postfix(Task __result)
    {
        await __result;

        try
        {
            if (!RunManager.Instance.IsSinglePlayerOrFakeMultiplayer)
                return;

            if (SingleplayerPreviousStepRestoreStateService.IsRestoreInProgress)
            {
                MainFile.Logger.Info("Skipped previous-step snapshot scheduling during restore.");
                return;
            }

            var combatState = CombatManager.Instance.DebugOnlyGetState();
            if (combatState == null || combatState.CurrentSide != CombatSide.Player)
                return;

            SingleplayerPreviousStepSnapshotCoordinator.ScheduleCaptureForCurrentTurn($"round_start_{combatState.RoundNumber}");
        }
        catch (System.Exception e)
        {
            MainFile.Logger.Error($"Failed to schedule previous-step snapshot on player turn start: {e}");
        }
    }
}
