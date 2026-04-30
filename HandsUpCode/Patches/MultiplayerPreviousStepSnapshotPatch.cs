using System.Threading.Tasks;
using HarmonyLib;
using HandsUp.HandsUpCode.Services;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;

namespace HandsUp.HandsUpCode.Patches;

[HarmonyPatch(typeof(CombatManager), "StartTurn")]
public static class CaptureMultiplayerPreviousStepSnapshotPatch
{
    public static async void Postfix(Task __result)
    {
        await __result;

        try
        {
            var netType = RunManager.Instance.NetService?.Type;
            if (netType != NetGameType.Host && netType != NetGameType.Client)
                return;

            if (MultiplayerPreviousStepRestoreStateService.IsRestoreInProgress)
            {
                MainFile.Logger.Info("Skipped multiplayer previous-step snapshot scheduling during restore.");
                return;
            }

            var combatState = CombatManager.Instance.DebugOnlyGetState();
            if (combatState == null || combatState.CurrentSide != CombatSide.Player)
                return;

            MultiplayerPreviousStepSnapshotCoordinator.ScheduleCaptureForCurrentTurn($"round_start_{combatState.RoundNumber}");
        }
        catch (System.Exception e)
        {
            MainFile.Logger.Error($"Failed to schedule multiplayer previous-step snapshot on player turn start: {e}");
        }
    }
}
