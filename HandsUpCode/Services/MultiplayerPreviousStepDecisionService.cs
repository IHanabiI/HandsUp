using HandsUp.HandsUpCode.UI;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace HandsUp.HandsUpCode.Services;

public static class MultiplayerPreviousStepDecisionService
{
    private const string MultiplayerCombatOnlyStatusText = "\u5f53\u524d\u7248\u672c\u7684\u8054\u673a\u56de\u5230\u4e0a\u4e00\u6b65\u5148\u53ea\u652f\u6301\u6218\u6597\u4e2d\u4f7f\u7528\u3002";
    private const string CombatFinishedStatusText = "\u6218\u6597\u5df2\u7ed3\u675f\u3002";

    public enum RestoreMode
    {
        None,
        RoomStart,
        Snapshot
    }

    public sealed class Decision
    {
        public bool CanProceed { get; init; }
        public bool ShouldShowPopup { get; init; }
        public string StatusText { get; init; } = string.Empty;
        public string PopupTitle { get; init; } = string.Empty;
        public string PopupBody { get; init; } = string.Empty;
        public RestoreMode Mode { get; init; }
        public int TargetRound { get; init; }
        public int CurrentRound { get; init; }
        public string RestoreHint { get; init; } = string.Empty;
        public MultiplayerBossElitePreviousStepService.CombatRoomKind RoomKind { get; init; }
    }

    public static Decision Evaluate(RunState? runState)
    {
        if (runState != null && MultiplayerRunStartStageService.IsAtNeowStage(runState))
        {
            return NoWayBack();
        }

        if (runState?.CurrentRoom is MapRoom)
        {
            return NoWayBack();
        }

        if (runState?.CurrentRoom is not CombatRoom combatRoom)
            return Blocked(MultiplayerCombatOnlyStatusText);

        if (combatRoom.IsPreFinished)
        {
            return Blocked(
                CombatFinishedStatusText,
                RaiseHandPopupText.CombatFinishedStepTitleText,
                RaiseHandPopupText.CombatFinishedStepBodyText);
        }

        var currentRound = combatRoom.CombatState.RoundNumber;
        if (currentRound <= 1)
        {
            return NoWayBack();
        }

        var roomKind = MultiplayerBossElitePreviousStepService.ResolveRoomKind(runState, combatRoom);
        if (currentRound == 2)
        {
            if (MultiplayerEventCombatPreviousStepService.ShouldTreatAsStandaloneCombat(combatRoom))
            {
                const int combatStartRound = 1;
                if (!MultiplayerPreviousStepSnapshotService.HasSnapshotForRound(combatStartRound))
                    return NoWayBack();

                return new Decision
                {
                    CanProceed = true,
                    Mode = RestoreMode.Snapshot,
                    TargetRound = combatStartRound,
                    CurrentRound = currentRound,
                    RestoreHint = "multiplayer event combat previous step",
                    RoomKind = roomKind
                };
            }

            return new Decision
            {
                CanProceed = true,
                Mode = RestoreMode.RoomStart,
                TargetRound = 1,
                CurrentRound = currentRound,
                RestoreHint = MultiplayerBossElitePreviousStepService.GetRoomStartRestoreHint(roomKind),
                RoomKind = roomKind
            };
        }

        var targetRound = currentRound - 1;
        if (!MultiplayerPreviousStepSnapshotService.HasSnapshotForRound(targetRound))
            return NoWayBack();

        return new Decision
        {
            CanProceed = true,
            Mode = RestoreMode.Snapshot,
            TargetRound = targetRound,
            CurrentRound = currentRound,
            RestoreHint = MultiplayerBossElitePreviousStepService.GetRestoreHint(roomKind),
            RoomKind = roomKind
        };
    }

    private static Decision NoWayBack()
    {
        return Blocked(
            RaiseHandPopupText.NoWayBackStepTitleText,
            RaiseHandPopupText.NoWayBackStepTitleText,
            RaiseHandPopupText.NoWayBackStepBodyText);
    }

    private static Decision Blocked(string statusText, string? popupTitle = null, string? popupBody = null)
    {
        return new Decision
        {
            CanProceed = false,
            ShouldShowPopup = !string.IsNullOrWhiteSpace(popupTitle) && !string.IsNullOrWhiteSpace(popupBody),
            StatusText = statusText,
            PopupTitle = popupTitle ?? string.Empty,
            PopupBody = popupBody ?? string.Empty,
            Mode = RestoreMode.None
        };
    }
}
