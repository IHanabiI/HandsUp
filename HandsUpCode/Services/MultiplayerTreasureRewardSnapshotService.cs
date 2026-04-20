using System.Linq;
using System.Reflection;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace HandsUp.HandsUpCode.Services;

public static class MultiplayerTreasureRewardSnapshotService
{
    private static readonly FieldInfo? CurrentRelicsField = typeof(TreasureRoomRelicSynchronizer)
        .GetField("_currentRelics", BindingFlags.Instance | BindingFlags.NonPublic);

    public static bool ApplySnapshotIfNeeded(
        RunManager? runManager,
        MultiplayerSoftRestartSnapshotService.MultiplayerSoftRestartSnapshot? snapshot,
        string expectedRoomScope,
        string restoreContext)
    {
        var runState = runManager?.DebugOnlyGetState();
        if (runManager == null
            || runState?.CurrentRoom is not TreasureRoom
            || snapshot?.TreasureRelicIds == null
            || snapshot.TreasureRelicIds.Count == 0
            || snapshot.RoomScope != expectedRoomScope)
        {
            return false;
        }

        if (CurrentRelicsField == null)
        {
            MainFile.Logger.Warn($"Skipped treasure reward snapshot restore for {restoreContext} because TreasureRoomRelicSynchronizer._currentRelics was not found.");
            return false;
        }

        try
        {
            var relics = snapshot.TreasureRelicIds
                .Select(ModelId.Deserialize)
                .Select(ModelDb.GetById<RelicModel>)
                .ToList();
            CurrentRelicsField.SetValue(runManager.TreasureRoomRelicSynchronizer, relics);
            MainFile.Logger.Info($"Applied multiplayer treasure reward snapshot for {restoreContext}: {string.Join(", ", snapshot.TreasureRelicIds)}");
            return true;
        }
        catch (System.Exception e)
        {
            MainFile.Logger.Error($"Failed to apply multiplayer treasure reward snapshot for {restoreContext}: {e}");
            return false;
        }
    }
}
