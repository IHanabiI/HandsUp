using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Map;

namespace HandsUp.HandsUpCode.Services;

public static class MultiplayerSoftRestartRoomClassifier
{
    public static bool IsAncientRoom(RunState? runState)
    {
        var mapPointType = runState?.CurrentMapPointHistoryEntry?.MapPointType
                           ?? runState?.CurrentMapPoint?.PointType
                           ?? MapPointType.Unassigned;

        return mapPointType == MapPointType.Ancient;
    }

    public static bool IsEventScopedRoom(RunState? runState)
    {
        if (runState?.CurrentRoom == null)
            return false;

        if (IsAncientRoom(runState))
            return true;

        if (runState.CurrentRoom is EventRoom)
            return true;

        if (runState.CurrentRoom is not CombatRoom combatRoom || combatRoom.IsPreFinished)
            return false;

        if (combatRoom.ParentEventId != null)
            return true;

        return runState.CurrentRoomCount > 1 && runState.BaseRoom is EventRoom;
    }

    public static bool ShouldUseInitialCombatPileSnapshot(RunState? runState)
    {
        return runState?.CurrentRoom is CombatRoom combatRoom
               && !combatRoom.IsPreFinished
               && !IsEventScopedRoom(runState);
    }
}
