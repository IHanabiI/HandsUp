using HandsUp.HandsUpCode.Multiplayer;
using HandsUp.HandsUpCode.UI;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace HandsUp.HandsUpCode.Services;

public static class MultiplayerApprovalPrecheckService
{
    private const string StateChangedStatusText = "\u5f53\u524d\u72b6\u6001\u5df2\u53d1\u751f\u53d8\u5316\uff0c\u8bf7\u91cd\u65b0\u53d1\u8d77\u3002";

    public sealed class Decision
    {
        public bool CanProceed { get; init; }
        public bool ShouldShowPopup { get; init; }
        public string StatusText { get; init; } = string.Empty;
        public string PopupTitle { get; init; } = string.Empty;
        public string PopupBody { get; init; } = string.Empty;
    }

    public static Decision Evaluate(RaiseHandActionKind actionKind, RunState? runState)
    {
        return actionKind switch
        {
            RaiseHandActionKind.SoftRestart => EvaluateSoftRestart(runState),
            RaiseHandActionKind.PreviousStep => FromPreviousStepDecision(MultiplayerPreviousStepDecisionService.Evaluate(runState)),
            RaiseHandActionKind.PreviousFloor => EvaluatePreviousFloor(runState),
            _ => Allowed()
        };
    }

    public static Decision StateChanged()
    {
        return Blocked(
            StateChangedStatusText,
            RaiseHandPopupText.ApprovalStateChangedTitleText,
            RaiseHandPopupText.ApprovalStateChangedBodyText);
    }

    private static Decision EvaluateSoftRestart(RunState? runState)
    {
        if (runState?.CurrentRoom is MapRoom && !MultiplayerRunStartStageService.IsAtNeowStage(runState))
        {
            return Blocked(
                RaiseHandPopupText.MapOnlyTitleText,
                RaiseHandPopupText.MapOnlyTitleText,
                RaiseHandPopupText.MapOnlyBodyText);
        }

        return Allowed();
    }

    private static Decision EvaluatePreviousFloor(RunState? runState)
    {
        if (runState != null
            && (MultiplayerRunStartStageService.IsAtNeowStage(runState) || runState.TotalFloor <= 1))
        {
            return NoWayBack();
        }

        if (!MultiplayerPreviousFloorSnapshotService.HasSnapshot())
        {
            return NoWayBack();
        }

        return Allowed();
    }

    private static Decision FromPreviousStepDecision(MultiplayerPreviousStepDecisionService.Decision decision)
    {
        return new Decision
        {
            CanProceed = decision.CanProceed,
            ShouldShowPopup = decision.ShouldShowPopup,
            StatusText = decision.StatusText,
            PopupTitle = decision.PopupTitle,
            PopupBody = decision.PopupBody
        };
    }

    private static Decision Allowed()
    {
        return new Decision
        {
            CanProceed = true
        };
    }

    private static Decision NoWayBack()
    {
        return Blocked(
            RaiseHandPopupText.NoWayBackTitleText,
            RaiseHandPopupText.NoWayBackTitleText,
            RaiseHandPopupText.NoWayBackBodyText);
    }

    private static Decision Blocked(string statusText, string? popupTitle = null, string? popupBody = null)
    {
        return new Decision
        {
            CanProceed = false,
            ShouldShowPopup = !string.IsNullOrWhiteSpace(popupTitle) && !string.IsNullOrWhiteSpace(popupBody),
            StatusText = statusText,
            PopupTitle = popupTitle ?? string.Empty,
            PopupBody = popupBody ?? string.Empty
        };
    }
}
