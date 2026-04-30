using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace HandsUp.HandsUpCode.Services;

public static class MultiplayerRunStartStageService
{
    public const string NeowRunStartRestoreHint = "multiplayer_neow_run_start";

    public static bool IsAtNeowStage(RunState? runState)
    {
        if (runState == null || runState.CurrentActIndex != 0)
            return false;

        if (runState.CurrentRoom is EventRoom eventRoom)
        {
            var modelEntry = eventRoom.ModelId?.Entry;
            if (!string.IsNullOrWhiteSpace(modelEntry)
                && modelEntry.Contains("NEOW", System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        if (runState.CurrentRoom is MapRoom
            && runState.CurrentMapCoord == null
            && runState.ActFloor == (runState.ExtraFields.StartedWithNeow ? 1 : 0))
        {
            return true;
        }

        return false;
    }
}
