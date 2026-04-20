using System.Threading.Tasks;
using HarmonyLib;
using HandsUp.HandsUpCode.Services;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace HandsUp.HandsUpCode.Patches;

[HarmonyPatch(typeof(CombatManager), "StartTurn")]
public static class CapturePreviousStepOnTurnStartedPatch
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
                MainFile.Logger.Info("Skipped previous-step snapshot turn-start handling during singleplayer previous-step restore.");
                return;
            }

            var combatState = CombatManager.Instance.DebugOnlyGetState();
            var currentRoom = combatState?.RunState?.CurrentRoom as CombatRoom;
            if (combatState == null || currentRoom == null || currentRoom.IsPreFinished || combatState.CurrentSide != CombatSide.Player)
                return;

            if (combatState.RoundNumber <= 1)
            {
                if (NonStandardSingleplayerRunIdentity.ShouldUseIsolatedSnapshots(RunManager.Instance))
                    NonStandardPreviousStepSnapshotService.ClearSnapshots(RunManager.Instance, "entered combat first round");
                else
                    PreviousStepSnapshotService.ClearSnapshots("entered combat first round");

                return;
            }

            if (NonStandardSingleplayerRunIdentity.ShouldUseIsolatedSnapshots(RunManager.Instance))
                NonStandardPreviousStepSnapshotService.CaptureSnapshotFromCurrentCombatStep(RunManager.Instance, $"round_start_{combatState.RoundNumber}");
            else
                PreviousStepSnapshotService.CaptureSnapshotFromCurrentCombatStep(RunManager.Instance, $"round_start_{combatState.RoundNumber}");
        }
        catch (System.Exception e)
        {
            MainFile.Logger.Error($"Failed to capture previous-step snapshot on player turn start: {e}");
        }
    }
}
