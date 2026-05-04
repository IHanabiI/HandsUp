using System.Linq;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace HandsUp.HandsUpCode.Services;

public static class SingleplayerBossElitePreviousStepService
{
    public enum CombatRoomKind
    {
        Normal,
        Elite,
        Boss
    }

    public static CombatRoomKind ResolveRoomKind(RunState? runState, CombatRoom combatRoom)
    {
        if (combatRoom.IsPreFinished)
            return CombatRoomKind.Normal;

        var mapPointType = runState?.CurrentMapPointHistoryEntry?.MapPointType
                           ?? runState?.CurrentMapPoint?.PointType
                           ?? MapPointType.Unassigned;

        return mapPointType switch
        {
            MapPointType.Elite => CombatRoomKind.Elite,
            MapPointType.Boss => CombatRoomKind.Boss,
            _ => CombatRoomKind.Normal
        };
    }

    public static string GetRestoreContext(CombatRoomKind roomKind)
    {
        return roomKind switch
        {
            CombatRoomKind.Elite => "singleplayer elite previous step",
            CombatRoomKind.Boss => "singleplayer boss previous step",
            _ => "singleplayer previous step"
        };
    }

    public static string GetRestoreStatusText(CombatRoomKind roomKind)
    {
        return roomKind switch
        {
            CombatRoomKind.Elite => "正在回退到上一步（精英房）…",
            CombatRoomKind.Boss => "正在回退到上一步（Boss房）…",
            _ => "正在回退到上一步…"
        };
    }

    public static string GetRoomStartStatusText(CombatRoomKind roomKind)
    {
        return roomKind switch
        {
            CombatRoomKind.Elite => "正在回退到精英房进入时的状态…",
            CombatRoomKind.Boss => "正在回退到Boss房进入时的状态…",
            _ => "正在回退到进入房间时的状态…"
        };
    }

    public static void LogRestoreRequested(CombatRoomKind roomKind, CombatRoom combatRoom, int currentRound, int targetRound)
    {
        MainFile.Logger.Info(
            $"Starting {DescribeRoomKind(roomKind)} previous-step restore. " +
            $"currentRound={currentRound} targetRound={targetRound} encounter={DescribeEncounter(combatRoom)} " +
            $"monsters={DescribeMonsters(combatRoom)}");
    }

    public static void LogRoomStartRestoreRequested(CombatRoomKind roomKind, CombatRoom combatRoom, int currentRound)
    {
        MainFile.Logger.Info(
            $"Returning {DescribeRoomKind(roomKind)} previous-step to room-start state. " +
            $"currentRound={currentRound} encounter={DescribeEncounter(combatRoom)} monsters={DescribeMonsters(combatRoom)}");
    }

    public static void LogRestoreCompleted(CombatRoomKind roomKind, RunState? runState)
    {
        var combatRoom = runState?.CurrentRoom as CombatRoom;
        var combatState = combatRoom?.CombatState;
        var combatManager = CombatManager.Instance;

        MainFile.Logger.Info(
            $"Completed {DescribeRoomKind(roomKind)} previous-step restore. " +
            $"encounter={DescribeEncounter(combatRoom)} " +
            $"round={(combatState?.RoundNumber.ToString() ?? "unknown")} " +
            $"currentSide={(combatState?.CurrentSide.ToString() ?? "unknown")} " +
            $"isPlayPhase={CombatStateCompatibilityService.IsPlayPhase(combatState)} " +
            $"phase={CombatStateCompatibilityService.DescribePlayerTurnPhases(combatState)} " +
            $"playerActionsDisabled={(combatManager?.PlayerActionsDisabled.ToString() ?? "unknown")} " +
            $"monsters={DescribeMonsters(combatRoom)}");
    }

    private static string DescribeRoomKind(CombatRoomKind roomKind)
    {
        return roomKind switch
        {
            CombatRoomKind.Elite => "elite",
            CombatRoomKind.Boss => "boss",
            _ => "normal"
        };
    }

    private static string DescribeEncounter(CombatRoom? combatRoom)
    {
        if (combatRoom == null)
            return "unknown";

        return combatRoom.ModelId?.ToString() ?? combatRoom.RoomType.ToString();
    }

    private static string DescribeMonsters(CombatRoom? combatRoom)
    {
        if (combatRoom?.CombatState == null)
            return "none";

        var monsterDescriptions = combatRoom.CombatState.Creatures
            .Where(creature => creature.Player == null)
            .Select(creature =>
            {
                var monsterId = creature.Monster?.Id.Entry ?? "unknown";
                return string.IsNullOrWhiteSpace(creature.SlotName) ? monsterId : $"{monsterId}:{creature.SlotName}";
            })
            .ToList();

        return monsterDescriptions.Count > 0 ? string.Join(", ", monsterDescriptions) : "none";
    }
}
