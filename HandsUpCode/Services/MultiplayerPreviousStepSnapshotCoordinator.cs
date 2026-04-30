using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace HandsUp.HandsUpCode.Services;

public static class MultiplayerPreviousStepSnapshotCoordinator
{
    private const int CaptureMaxFrames = 600;
    private const int CaptureStableFramesRequired = 3;

    private static readonly FieldInfo? HandDisabledField = typeof(NPlayerHand)
        .GetField("_isDisabled", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly MethodInfo? HandAreCardActionsAllowedMethod = typeof(NPlayerHand)
        .GetMethod("AreCardActionsAllowed", BindingFlags.Instance | BindingFlags.NonPublic);

    private static int _captureRequestId;

    public static void ScheduleCaptureForCurrentTurn(string sourceTag)
    {
        var requestId = ++_captureRequestId;
        MainFile.Logger.Info($"Scheduled multiplayer previous-step snapshot capture request {requestId} from {sourceTag}.");
        _ = CaptureWhenStaticAsync(requestId, sourceTag);
    }

    private static async Task CaptureWhenStaticAsync(int requestId, string sourceTag)
    {
        try
        {
            StaticBoundaryMarker? previousMarker = null;
            var stableFrameCount = 0;
            string? lastFailureReason = null;

            for (var frame = 0; frame < CaptureMaxFrames; frame++)
            {
                await Engine.GetMainLoop().ToSignal(Engine.GetMainLoop(), SceneTree.SignalName.ProcessFrame);

                if (requestId != _captureRequestId)
                    return;

                if (MultiplayerPreviousStepRestoreStateService.IsRestoreInProgress)
                {
                    MainFile.Logger.Info($"Cancelled multiplayer previous-step snapshot capture request {requestId} because restore started.");
                    return;
                }

                if (!TryBuildStaticBoundaryMarker(RunManager.Instance, out var marker, out var failureReason))
                {
                    if (!string.Equals(lastFailureReason, failureReason, System.StringComparison.Ordinal)
                        || frame == CaptureMaxFrames - 1)
                    {
                        MainFile.Logger.Info($"Multiplayer previous-step snapshot capture request {requestId} is waiting for static boundary: {failureReason}");
                    }

                    lastFailureReason = failureReason;
                    previousMarker = null;
                    stableFrameCount = 0;
                    continue;
                }

                var currentCombatRoom = RunManager.Instance.DebugOnlyGetState()?.CurrentRoom as CombatRoom;
                if (marker!.RoundNumber <= 1
                    && !MultiplayerEventCombatPreviousStepService.ShouldTreatAsStandaloneCombat(currentCombatRoom))
                {
                    MultiplayerPreviousStepSnapshotService.ClearSnapshots("entered multiplayer combat first round");
                    return;
                }

                if (previousMarker != null && AreEquivalent(previousMarker, marker))
                    stableFrameCount++;
                else
                    stableFrameCount = 0;

                previousMarker = marker;
                if (stableFrameCount >= CaptureStableFramesRequired)
                {
                    MainFile.Logger.Info(
                        $"Multiplayer previous-step snapshot boundary stabilized after {frame + 1} frame(s): " +
                        $"round={marker.RoundNumber} nextActionId={marker.NextActionId} nextHookId={marker.NextHookId}.");
                    MultiplayerPreviousStepSnapshotService.CaptureSnapshotFromCurrentCombatStep(RunManager.Instance, $"{sourceTag}_static");
                    return;
                }
            }

            if (previousMarker != null)
            {
                MainFile.Logger.Warn(
                    $"Multiplayer previous-step snapshot boundary did not fully stabilize in time; capturing latest observed static state. " +
                    $"round={previousMarker.RoundNumber} nextActionId={previousMarker.NextActionId} nextHookId={previousMarker.NextHookId}.");
                MultiplayerPreviousStepSnapshotService.CaptureSnapshotFromCurrentCombatStep(RunManager.Instance, $"{sourceTag}_fallback");
                return;
            }

            if (TryBuildPlayableBoundaryMarker(RunManager.Instance, out var relaxedMarker, out var relaxedFailureReason))
            {
                MainFile.Logger.Warn(
                    $"Multiplayer previous-step snapshot capture request {requestId} timed out before full static boundary; " +
                    $"using relaxed playable fallback for round {relaxedMarker!.RoundNumber}.");
                MultiplayerPreviousStepSnapshotService.CaptureSnapshotFromCurrentCombatStep(RunManager.Instance, $"{sourceTag}_playable_fallback");
                return;
            }

            MainFile.Logger.Warn(
                $"Multiplayer previous-step snapshot capture request {requestId} timed out without any capturable boundary. " +
                $"Last strict failure={lastFailureReason ?? "unknown"}, last relaxed failure={relaxedFailureReason}.");
        }
        catch (System.Exception e)
        {
            MainFile.Logger.Error($"Failed during multiplayer previous-step snapshot capture request {requestId} from {sourceTag}: {e}");
        }
    }

    private static bool TryBuildStaticBoundaryMarker(RunManager? runManager, out StaticBoundaryMarker? marker, out string failureReason)
    {
        marker = null;
        failureReason = string.Empty;

        var runState = runManager?.DebugOnlyGetState();
        var combatState = CombatManager.Instance.DebugOnlyGetState();
        var netType = runManager?.NetService?.Type;
        if (runManager == null
            || runState == null
            || combatState == null
            || (netType != NetGameType.Host && netType != NetGameType.Client))
        {
            failureReason = "run state unavailable";
            return false;
        }

        if (runState.CurrentRoom is not CombatRoom combatRoom || combatRoom.IsPreFinished)
        {
            failureReason = "current room is not an active combat room";
            return false;
        }

        if (!IsAtStaticDecisionBoundary(runManager, combatState, out failureReason))
            return false;

        marker = BuildMarker(runState, combatRoom, runManager, combatState);
        return marker != null;
    }

    private static bool TryBuildPlayableBoundaryMarker(RunManager? runManager, out StaticBoundaryMarker? marker, out string failureReason)
    {
        marker = null;
        failureReason = string.Empty;

        var runState = runManager?.DebugOnlyGetState();
        var combatState = CombatManager.Instance.DebugOnlyGetState();
        var netType = runManager?.NetService?.Type;
        if (runManager == null
            || runState == null
            || combatState == null
            || (netType != NetGameType.Host && netType != NetGameType.Client))
        {
            failureReason = "run state unavailable";
            return false;
        }

        if (runState.CurrentRoom is not CombatRoom combatRoom || combatRoom.IsPreFinished)
        {
            failureReason = "current room is not an active combat room";
            return false;
        }

        if (!IsAtPlayableDecisionBoundary(runManager, combatState, out failureReason))
            return false;

        marker = BuildMarker(runState, combatRoom, runManager, combatState);
        return marker != null;
    }

    private static StaticBoundaryMarker? BuildMarker(
        RunState runState,
        CombatRoom combatRoom,
        RunManager runManager,
        CombatState combatState)
    {
        var roomIdentity = combatRoom.ModelId?.ToString() ?? combatRoom.RoomType.ToString();
        return new StaticBoundaryMarker
        {
            RoomScopeKey = $"{runState.TotalFloor:D4}_{roomIdentity}",
            RoundNumber = combatState.RoundNumber,
            NextActionId = runManager.ActionQueueSet.NextActionId,
            NextHookId = runManager.ActionQueueSynchronizer.NextHookId,
            NextChecksumId = runManager.ChecksumTracker.NextId,
            ChoiceIds = runManager.PlayerChoiceSynchronizer.ChoiceIds.ToList()
        };
    }

    private static bool IsAtStaticDecisionBoundary(RunManager runManager, CombatState combatState, out string failureReason)
    {
        if (!IsAtPlayableDecisionBoundary(runManager, combatState, out failureReason))
            return false;

        if (runManager.ActionExecutor.IsPaused)
        {
            failureReason = "action executor is paused";
            return false;
        }

        if (runManager.ActionExecutor.IsRunning)
        {
            failureReason = "action executor is still running";
            return false;
        }

        if (runManager.ActionExecutor.CurrentlyRunningAction != null)
        {
            failureReason = $"currently running action is {runManager.ActionExecutor.CurrentlyRunningAction.GetType().Name}";
            return false;
        }

        if (!runManager.ActionQueueSet.IsEmpty)
        {
            failureReason = "action queue is not empty";
            return false;
        }

        failureReason = string.Empty;
        return true;
    }

    private static bool IsAtPlayableDecisionBoundary(RunManager runManager, CombatState combatState, out string failureReason)
    {
        if (combatState.CurrentSide != CombatSide.Player)
        {
            failureReason = $"current side is {combatState.CurrentSide}";
            return false;
        }

        if (!CombatManager.Instance.IsInProgress)
        {
            failureReason = "combat is not in progress";
            return false;
        }

        if (!CombatManager.Instance.IsPlayPhase)
        {
            failureReason = "combat is not in play phase";
            return false;
        }

        if (CombatManager.Instance.EndingPlayerTurnPhaseOne || CombatManager.Instance.EndingPlayerTurnPhaseTwo)
        {
            failureReason = "player turn is ending";
            return false;
        }

        if (CombatManager.Instance.PlayerActionsDisabled)
        {
            failureReason = "player actions are disabled";
            return false;
        }

        if (runManager.ActionQueueSynchronizer.CombatState != ActionSynchronizerCombatState.PlayPhase)
        {
            failureReason = $"action queue synchronizer is {runManager.ActionQueueSynchronizer.CombatState}";
            return false;
        }

        if (!IsFalseOrMissing(GetHandDisabledState()))
        {
            failureReason = "hand UI is disabled";
            return false;
        }

        if (!IsTrueOrMissing(GetHandActionsAllowedState()))
        {
            failureReason = "hand UI is not allowing card actions";
            return false;
        }

        failureReason = string.Empty;
        return true;
    }

    private static bool AreEquivalent(StaticBoundaryMarker left, StaticBoundaryMarker right)
    {
        return left.RoomScopeKey == right.RoomScopeKey
               && left.RoundNumber == right.RoundNumber
               && left.NextActionId == right.NextActionId
               && left.NextHookId == right.NextHookId
               && left.NextChecksumId == right.NextChecksumId
               && left.ChoiceIds.SequenceEqual(right.ChoiceIds);
    }

    private static bool IsFalseOrMissing(bool? value)
    {
        return !value.HasValue || !value.Value;
    }

    private static bool IsTrueOrMissing(bool? value)
    {
        return !value.HasValue || value.Value;
    }

    private static bool? GetHandDisabledState()
    {
        return ReadBooleanField(HandDisabledField, NPlayerHand.Instance);
    }

    private static bool? GetHandActionsAllowedState()
    {
        return InvokeBooleanMethod(HandAreCardActionsAllowedMethod, NPlayerHand.Instance);
    }

    private static bool? ReadBooleanField(FieldInfo? field, object? target)
    {
        if (field == null || target == null)
            return null;

        try
        {
            var rawValue = field.GetValue(target);
            return rawValue is bool value ? value : rawValue as bool?;
        }
        catch
        {
            return null;
        }
    }

    private static bool? InvokeBooleanMethod(MethodInfo? method, object? target)
    {
        if (method == null || target == null)
            return null;

        try
        {
            var rawValue = method.Invoke(target, null);
            return rawValue is bool value ? value : rawValue as bool?;
        }
        catch
        {
            return null;
        }
    }

    private sealed class StaticBoundaryMarker
    {
        public string RoomScopeKey { get; set; } = string.Empty;
        public int RoundNumber { get; set; }
        public uint NextActionId { get; set; }
        public uint NextHookId { get; set; }
        public uint NextChecksumId { get; set; }
        public List<uint> ChoiceIds { get; set; } = [];
    }
}
