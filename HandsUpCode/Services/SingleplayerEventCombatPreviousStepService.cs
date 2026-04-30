using System.Linq;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace HandsUp.HandsUpCode.Services;

public static class SingleplayerEventCombatPreviousStepService
{
    public static bool ShouldTreatAsStandaloneCombat(CombatRoom? combatRoom)
    {
        return combatRoom?.ParentEventId != null
               && !combatRoom.IsPreFinished
               && !combatRoom.ShouldResumeParentEventAfterCombat;
    }

    public static SerializableRoom CreateRoomSnapshot(AbstractRoom room)
    {
        if (room is CombatRoom combatRoom && ShouldTreatAsStandaloneCombat(combatRoom))
            return CreateStandaloneCombatSnapshot(combatRoom);

        return room.ToSerializable();
    }

    private static SerializableRoom CreateStandaloneCombatSnapshot(CombatRoom combatRoom)
    {
        var snapshot = new SerializableRoom
        {
            RoomType = combatRoom.RoomType,
            EncounterId = combatRoom.Encounter.Id,
            IsPreFinished = combatRoom.IsPreFinished,
            GoldProportion = combatRoom.GoldProportion,
            ParentEventId = combatRoom.ParentEventId,
            ShouldResumeParentEvent = combatRoom.ShouldResumeParentEventAfterCombat,
            EncounterState = combatRoom.Encounter.SaveCustomState()
        };

        foreach (var (player, rewards) in combatRoom.ExtraRewards)
        {
            snapshot.ExtraRewards[player.NetId] = rewards
                .Select(static reward => reward.ToSerializable())
                .ToList();
        }

        return snapshot;
    }
}
