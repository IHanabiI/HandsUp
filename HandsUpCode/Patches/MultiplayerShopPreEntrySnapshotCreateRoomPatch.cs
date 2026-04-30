using HarmonyLib;
using HandsUp.HandsUpCode.Services;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace HandsUp.HandsUpCode.Patches;

[HarmonyPatch(typeof(RunManager), "CreateRoom")]
public static class MultiplayerShopPreEntrySnapshotCreateRoomPatch
{
    public static void Prefix(RoomType roomType)
    {
        if (roomType != RoomType.Shop)
            return;

        try
        {
            MultiplayerSoftRestartSnapshotService.CaptureShopPreEntrySnapshotFromCurrentState(
                RunManager.Instance,
                "create_room/shop_pre_entry");
        }
        catch (System.Exception e)
        {
            MainFile.Logger.Error($"Failed to capture multiplayer shop pre-entry soft-restart snapshot: {e}");
        }
    }
}
