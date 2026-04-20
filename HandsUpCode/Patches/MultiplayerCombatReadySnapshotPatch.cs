using System.Threading.Tasks;
using HarmonyLib;
using HandsUp.HandsUpCode.Services;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Runs;

namespace HandsUp.HandsUpCode.Patches;

[HarmonyPatch(typeof(CombatManager), "StartTurn")]
public static class CaptureMultiplayerSoftRestartSnapshotOnCombatReadyPatch
{
    public static async void Postfix(Task __result)
    {
        await __result;

        try
        {
            var runState = RunManager.Instance.DebugOnlyGetState();
            if (MultiplayerSoftRestartRoomClassifier.IsEventScopedRoom(runState))
                return;

            var combatState = CombatManager.Instance.DebugOnlyGetState();
            if (combatState == null || combatState.CurrentSide != CombatSide.Player || combatState.RoundNumber != 1)
                return;

            MultiplayerInitialCombatPileSnapshotService.CaptureSnapshotFromCurrentState(RunManager.Instance, "combat_ready");
        }
        catch (System.Exception e)
        {
            MainFile.Logger.Error($"Failed to capture multiplayer combat-ready snapshot: {e}");
        }
    }
}
