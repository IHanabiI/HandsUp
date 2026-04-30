using System.Linq;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace HandsUp.HandsUpCode.Services;

public static class MultiplayerBossElitePreviousStepService
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

    public static string GetRestoreHint(CombatRoomKind roomKind)
    {
        return roomKind switch
        {
            CombatRoomKind.Elite => "multiplayer elite previous step",
            CombatRoomKind.Boss => "multiplayer boss previous step",
            _ => "multiplayer previous step"
        };
    }

    public static string GetRoomStartRestoreHint(CombatRoomKind roomKind)
    {
        return roomKind switch
        {
            CombatRoomKind.Elite => "multiplayer elite room-start previous step",
            CombatRoomKind.Boss => "multiplayer boss room-start previous step",
            _ => "multiplayer room-start previous step"
        };
    }

    public static void LogRestoreRequested(CombatRoomKind roomKind, CombatRoom combatRoom, int currentRound, int targetRound)
    {
        MainFile.Logger.Info(
            $"Starting {DescribeRoomKind(roomKind)} multiplayer previous-step restore. " +
            $"currentRound={currentRound} targetRound={targetRound} encounter={DescribeEncounter(combatRoom)} " +
            $"monsters={DescribeMonsters(combatRoom)}");
    }

    public static void LogRoomStartRestoreRequested(CombatRoomKind roomKind, CombatRoom combatRoom, int currentRound)
    {
        MainFile.Logger.Info(
            $"Returning {DescribeRoomKind(roomKind)} multiplayer previous-step to room-start state. " +
            $"currentRound={currentRound} encounter={DescribeEncounter(combatRoom)} monsters={DescribeMonsters(combatRoom)}");
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
