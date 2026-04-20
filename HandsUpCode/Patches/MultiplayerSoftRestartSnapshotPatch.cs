using System.Threading.Tasks;
using HarmonyLib;
using HandsUp.HandsUpCode.Services;
using MegaCrit.Sts2.Core.Runs;

namespace HandsUp.HandsUpCode.Patches;

[HarmonyPatch(typeof(RunManager), nameof(RunManager.EnterRoom))]
public static class CaptureMultiplayerSoftRestartSnapshotOnEnterRoomPatch
{
    public static async void Postfix(Task __result)
    {
        await __result;

        try
        {
            MultiplayerSoftRestartSnapshotService.CaptureSnapshotFromCurrentState(RunManager.Instance, "enter_room");
        }
        catch (System.Exception e)
        {
            MainFile.Logger.Error($"Failed to capture multiplayer soft-restart snapshot after entering room: {e}");
        }
    }
}
