using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Replay;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Audio;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Managers;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace HandsUp.HandsUpCode.Services;

public static class SingleplayerPreviousStepReplayService
{
    private const string SessionDirectoryName = "handsup_previous_step_replay";
    private const string MetadataFileName = "session.json";
    private const string ReplayFileName = "session.mcr";
    private const string LegacySnapshotDirectoryName = "handsup_previous_step_history";
    private const string LegacyNonStandardSnapshotDirectoryName = "handsup_nonstandard_previous_step_history";
    private const int FadeDurationMs = 800;
    private const int MarkerCaptureMaxFrames = 24;
    private const int MarkerCaptureStableFramesRequired = 3;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    private static readonly FieldInfo? ReplayWriterReplayField = typeof(CombatReplayWriter)
        .GetField("_replay", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? HandDisabledField = typeof(NPlayerHand)
        .GetField("_isDisabled", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly MethodInfo? HandOnCombatStateChangedMethod = typeof(NPlayerHand)
        .GetMethod("OnCombatStateChanged", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly MethodInfo? HandCanPlayCardsMethod = typeof(NPlayerHand)
        .GetMethod("CanPlayCards", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly MethodInfo? HandAreCardActionsAllowedMethod = typeof(NPlayerHand)
        .GetMethod("AreCardActionsAllowed", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? EndTurnStateField = typeof(NEndTurnButton)
        .GetField("_state", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly MethodInfo? EndTurnButtonOnTurnStartedMethod = typeof(NEndTurnButton)
        .GetMethod("OnTurnStarted", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly PropertyInfo? EndTurnCanBeEndedProperty = typeof(NEndTurnButton)
        .GetProperty("CanTurnBeEnded", BindingFlags.Instance | BindingFlags.NonPublic);
    private static int _captureRequestId;

    public static void ScheduleTurnMarkerCapture(string sourceTag)
    {
        var requestId = ++_captureRequestId;
        _ = CaptureTurnMarkerWhenStableAsync(requestId, sourceTag);
    }

    public static void CaptureTurnMarker(RunManager? runManager, string sourceTag)
    {
        try
        {
            if (!TryBuildCurrentMarker(runManager, out var marker))
                return;

            var replay = CloneCurrentReplay(runManager?.CombatReplayWriter);
            if (replay == null)
                return;

            ClearLegacySnapshotArtifacts();

            var metadata = LoadMetadata();
            if (metadata == null || metadata.RoomScopeKey != marker.RoomScopeKey)
                metadata = new ReplaySessionMetadata { RoomScopeKey = marker.RoomScopeKey };

            metadata.Markers.RemoveAll(existing => existing.RoundNumber == marker.RoundNumber);
            metadata.Markers.Add(marker);
            metadata.Markers.Sort((left, right) => left.RoundNumber.CompareTo(right.RoundNumber));

            SaveSession(metadata, replay);
            MainFile.Logger.Info(
                $"Captured previous-step replay marker for round {marker.RoundNumber} from {sourceTag}. " +
                $"eventCount={marker.EventCount} checksumCount={marker.ChecksumCount} " +
                $"nextActionId={marker.NextActionId} nextHookId={marker.NextHookId} nextChecksumId={marker.NextChecksumId} " +
                $"choiceCount={marker.ChoiceIds.Count}");
            LogCurrentCombatSummary($"marker-captured:{sourceTag}");
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"Failed to capture previous-step replay marker: {e}");
        }
    }

    private static async Task CaptureTurnMarkerWhenStableAsync(int requestId, string sourceTag)
    {
        ReplayTurnMarker? previousMarker = null;
        var stableFrameCount = 0;

        for (var frame = 0; frame < MarkerCaptureMaxFrames; frame++)
        {
            await Engine.GetMainLoop().ToSignal(Engine.GetMainLoop(), SceneTree.SignalName.ProcessFrame);

            if (requestId != _captureRequestId)
                return;

            if (SingleplayerPreviousStepRestoreStateService.IsRestoreInProgress)
                return;

            if (!TryBuildCurrentMarker(RunManager.Instance, out var currentMarker))
            {
                previousMarker = null;
                stableFrameCount = 0;
                continue;
            }

            if (previousMarker != null && AreEquivalent(previousMarker, currentMarker))
                stableFrameCount++;
            else
                stableFrameCount = 0;

            previousMarker = currentMarker;

            if (stableFrameCount >= MarkerCaptureStableFramesRequired)
            {
                MainFile.Logger.Info(
                    $"Previous-step replay marker stabilized after {frame + 1} frame(s): " +
                    $"round={currentMarker!.RoundNumber} eventCount={currentMarker.EventCount} nextActionId={currentMarker.NextActionId}.");
                CaptureTurnMarker(RunManager.Instance, $"{sourceTag}_stable");
                return;
            }
        }

        if (previousMarker != null)
        {
            MainFile.Logger.Warn(
                $"Previous-step replay marker did not fully stabilize in time; using latest observed state. " +
                $"round={previousMarker.RoundNumber} eventCount={previousMarker.EventCount} nextActionId={previousMarker.NextActionId}.");
            CaptureTurnMarker(RunManager.Instance, $"{sourceTag}_fallback");
        }
    }

    public static bool HasReplayMarkerForCurrentCombat(RunManager? runManager, int roundNumber)
    {
        if (!TryGetValidatedSession(runManager, out var metadata, out _))
            return false;

        return metadata!.Markers.Any(marker => marker.RoundNumber == roundNumber);
    }

    public static async Task<bool> RestorePreviousRoundAsync(RunManager? runManager, int targetRound)
    {
        if (!TryGetValidatedSession(runManager, out var metadata, out var replay))
        {
            MainFile.Logger.Warn("Previous-step replay restore skipped because no valid replay session exists for the current combat.");
            return false;
        }

        var marker = metadata!.Markers.FirstOrDefault(candidate => candidate.RoundNumber == targetRound);
        if (marker == null)
        {
            MainFile.Logger.Warn($"Previous-step replay restore skipped because marker round {targetRound} was not found.");
            return false;
        }

        var replayCopy = CloneReplay(replay!);
        TruncateReplay(replayCopy, marker);
        MainFile.Logger.Info(
            $"Restoring previous-step replay to round {targetRound}. " +
            $"markerEventCount={marker.EventCount}/{replay.events.Count} markerChecksumCount={marker.ChecksumCount}/{replay.checksumData.Count} " +
            $"markerNextActionId={marker.NextActionId} markerNextHookId={marker.NextHookId} markerNextChecksumId={marker.NextChecksumId} " +
            $"markerChoiceCount={marker.ChoiceIds.Count}");
        LogExpectedMarkerState(replayCopy, targetRound);
        return await LoadPlayableReplayAtMarkerAsync(replayCopy, targetRound);
    }

    public static void ClearSession(string reason)
    {
        var sessionDirectory = ProjectSettings.GlobalizePath(GetSessionDirectoryPath());
        if (Directory.Exists(sessionDirectory))
            Directory.Delete(sessionDirectory, true);

        MainFile.Logger.Info($"Cleared previous-step replay session: {reason}");
    }

    private static bool TryBuildCurrentMarker(RunManager? runManager, out ReplayTurnMarker? marker)
    {
        marker = null;

        var runState = runManager?.DebugOnlyGetState();
        if (runManager == null || runState == null || !runManager.IsSinglePlayerOrFakeMultiplayer)
            return false;

        if (runState.CurrentRoom is not CombatRoom combatRoom || combatRoom.IsPreFinished)
            return false;

        if (runState.CurrentMapPoint?.PointType == MegaCrit.Sts2.Core.Map.MapPointType.Unassigned)
            return false;

        if (!IsAtStaticDecisionBoundary(runManager, combatRoom.CombatState))
            return false;

        var roomScopeKey = BuildRoomScopeKey(runState, combatRoom, runManager.ToSave(null));
        marker = new ReplayTurnMarker
        {
            RoundNumber = combatRoom.CombatState.RoundNumber,
            RoomScopeKey = roomScopeKey,
            EventCount = GetCurrentReplayEventCount(runManager.CombatReplayWriter),
            ChecksumCount = GetCurrentReplayChecksumCount(runManager.CombatReplayWriter),
            NextActionId = runManager.ActionQueueSet.NextActionId,
            NextHookId = runManager.ActionQueueSynchronizer.NextHookId,
            NextChecksumId = runManager.ChecksumTracker.NextId,
            ChoiceIds = runManager.PlayerChoiceSynchronizer.ChoiceIds.ToList()
        };
        return true;
    }

    private static bool IsAtStaticDecisionBoundary(RunManager runManager, CombatState combatState)
    {
        return combatState.CurrentSide == MegaCrit.Sts2.Core.Combat.CombatSide.Player
               && CombatManager.Instance.IsInProgress
               && CombatManager.Instance.IsPlayPhase
               && !CombatManager.Instance.EndingPlayerTurnPhaseOne
               && !CombatManager.Instance.EndingPlayerTurnPhaseTwo
               && !CombatManager.Instance.PlayerActionsDisabled
               && runManager.ActionQueueSynchronizer.CombatState == ActionSynchronizerCombatState.PlayPhase
               && !runManager.ActionExecutor.IsPaused
               && !runManager.ActionExecutor.IsRunning
               && runManager.ActionExecutor.CurrentlyRunningAction == null
               && runManager.ActionQueueSet.IsEmpty
               && IsFalseOrMissing(GetHandDisabledState())
               && IsTrueOrMissing(GetHandActionsAllowedState());
    }

    private static bool TryGetValidatedSession(
        RunManager? runManager,
        out ReplaySessionMetadata? metadata,
        out CombatReplay? replay)
    {
        metadata = null;
        replay = null;

        var runState = runManager?.DebugOnlyGetState();
        if (runManager == null || runState == null || !runManager.IsSinglePlayerOrFakeMultiplayer)
            return false;

        if (runState.CurrentRoom is not CombatRoom combatRoom || combatRoom.IsPreFinished)
            return false;

        metadata = LoadMetadata();
        replay = LoadReplay();
        if (metadata == null || replay == null)
            return false;

        var expectedRoomScopeKey = BuildRoomScopeKey(runState, combatRoom, runManager.ToSave(null));
        if (!string.Equals(metadata.RoomScopeKey, expectedRoomScopeKey, StringComparison.Ordinal))
            return false;

        return true;
    }

    private static string BuildRoomScopeKey(RunState runState, CombatRoom combatRoom, SerializableRun snapshot)
    {
        var roomIdentity = combatRoom.ModelId?.ToString() ?? combatRoom.RoomType.ToString();
        return $"{snapshot.StartTime:D16}_{runState.TotalFloor:D4}_{SanitizeFilePart(roomIdentity)}";
    }

    private static int GetCurrentReplayEventCount(CombatReplayWriter? replayWriter)
    {
        return TryGetCurrentReplay(replayWriter, out var replay) ? replay!.events.Count : 0;
    }

    private static int GetCurrentReplayChecksumCount(CombatReplayWriter? replayWriter)
    {
        return TryGetCurrentReplay(replayWriter, out var replay) ? replay!.checksumData.Count : 0;
    }

    private static bool TryGetCurrentReplay(CombatReplayWriter? replayWriter, out CombatReplay? replay)
    {
        replay = ReplayWriterReplayField?.GetValue(replayWriter) as CombatReplay;
        return replay != null;
    }

    private static CombatReplay? CloneCurrentReplay(CombatReplayWriter? replayWriter)
    {
        return TryGetCurrentReplay(replayWriter, out var replay) ? CloneReplay(replay!) : null;
    }

    private static CombatReplay CloneReplay(CombatReplay replay)
    {
        var writer = new PacketWriter();
        writer.Write(replay);
        writer.ZeroByteRemainder();

        var payload = writer.Buffer.AsSpan(0, writer.BytePosition).ToArray();
        var reader = new PacketReader();
        reader.Reset(payload);
        return reader.Read<CombatReplay>();
    }

    private static void TruncateReplay(CombatReplay replay, ReplayTurnMarker marker)
    {
        replay.events = replay.events.Take(marker.EventCount).ToList();
        replay.checksumData = replay.checksumData.Take(marker.ChecksumCount).ToList();
        replay.nextActionId = marker.NextActionId;
        replay.nextHookId = marker.NextHookId;
        replay.nextChecksumId = marker.NextChecksumId;
        replay.choiceIds = marker.ChoiceIds.ToList();
    }

    private static async Task<bool> LoadPlayableReplayAtMarkerAsync(CombatReplay replay, int targetRound)
    {
        RunState? runState = null;

        SingleplayerPreviousStepRestoreStateService.ClearPlayableReplaySession();
        SingleplayerPreviousStepRestoreStateService.BeginRestore();
        SingleplayerPreviousStepRestoreStateService.EnablePlayableReplaySession();

        try
        {
            runState = RunState.FromSerializable(replay.serializableRun);
            MainFile.Logger.Info(
                $"Starting playable replay restore. replayEvents={replay.events.Count} replayChecksums={replay.checksumData.Count} " +
                $"replayNextActionId={replay.nextActionId} replayNextHookId={replay.nextHookId} replayNextChecksumId={replay.nextChecksumId} " +
                $"replayChoiceCount={replay.choiceIds.Count}");

            NCapstoneContainer.Instance?.Close();
            if (NGame.Instance.CurrentRunNode != null)
                await NGame.Instance.Transition.FadeOut(FadeDurationMs / 1000f, "res://materials/transitions/fade_transition_mat.tres", null);

            RunManager.Instance.CleanUp(true);
            RunManager.Instance.SetUpReplay(runState, replay);
            RunManager.Instance.CombatStateSynchronizer.IsDisabled = true;

            await PreloadManager.LoadRunAssets(runState.Players.Select(player => player.Character));
            await PreloadManager.LoadActAssets(runState.Act);

            RunManager.Instance.Launch();
            NAudioManager.Instance?.StopMusic();
            NGame.Instance.RootSceneContainer.SetCurrentScene(NRun.Create(runState));

            await RunManager.Instance.GenerateMap();
            RunManager.Instance.ActionQueueSet.FastForwardNextActionId(replay.nextActionId);
            RunManager.Instance.ActionQueueSynchronizer.FastForwardHookId(replay.nextHookId);
            RunManager.Instance.ChecksumTracker.LoadReplayChecksums(replay.checksumData, replay.nextChecksumId);
            RunManager.Instance.PlayerChoiceSynchronizer.FastForwardChoiceIds(replay.choiceIds);
            await RunManager.Instance.LoadIntoLatestMapCoord(AbstractRoom.FromSerializable(replay.serializableRun.PreFinishedRoom, runState));

            while (RunManager.Instance.ActionExecutor.IsPaused)
                await Engine.GetMainLoop().ToSignal(Engine.GetMainLoop(), SceneTree.SignalName.ProcessFrame);

            foreach (var replayEvent in replay.events)
            {
                switch (replayEvent.eventType)
                {
                    case CombatReplayEventType.GameAction:
                    {
                        while (CombatManager.Instance.EndingPlayerTurnPhaseOne || CombatManager.Instance.EndingPlayerTurnPhaseTwo)
                            await Engine.GetMainLoop().ToSignal(Engine.GetMainLoop(), SceneTree.SignalName.ProcessFrame);

                        var player = runState.GetPlayer(replayEvent.playerId!.Value);
                        var action = replayEvent.action!.ToGameAction(player);
                        if (action.ActionType == GameActionType.CombatPlayPhaseOnly)
                        {
                            while (CombatManager.Instance.DebugOnlyGetState()?.CurrentSide == MegaCrit.Sts2.Core.Combat.CombatSide.Enemy)
                                await Engine.GetMainLoop().ToSignal(Engine.GetMainLoop(), SceneTree.SignalName.ProcessFrame);
                        }

                        RunManager.Instance.ActionQueueSet.EnqueueWithoutSynchronizing(action);
                        if (action is EndPlayerTurnAction or ReadyToBeginEnemyTurnAction)
                            await RunManager.Instance.ActionExecutor.FinishedExecutingActions();
                        break;
                    }
                    case CombatReplayEventType.HookAction:
                        RunManager.Instance.ActionQueueSet.EnqueueWithoutSynchronizing(
                            RunManager.Instance.ActionQueueSynchronizer.GetHookActionForId(
                                replayEvent.hookId!.Value,
                                replayEvent.playerId!.Value,
                                replayEvent.gameActionType!.Value));
                        break;
                    case CombatReplayEventType.ResumeAction:
                        RunManager.Instance.ActionQueueSet.ResumeActionWithoutSynchronizing(replayEvent.actionId!.Value);
                        break;
                    case CombatReplayEventType.PlayerChoice:
                    {
                        var player = runState.GetPlayer(replayEvent.playerId!.Value);
                        RunManager.Instance.PlayerChoiceSynchronizer.ReceiveReplayChoice(
                            player,
                            replayEvent.choiceId!.Value,
                            replayEvent.playerChoiceResult!.Value);
                        break;
                    }
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            if (!await WaitForPlayablePlayerTurnAsync(runState, targetRound))
                return false;

            LogCurrentCombatSummary($"restore-finished:round-{targetRound}");
            return true;
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"Previous-step replay restore failed: {e}");
            SingleplayerPreviousStepRestoreStateService.ClearPlayableReplaySession();
            return false;
        }
        finally
        {
            SingleplayerPreviousStepRestoreStateService.EndRestore();
            await NGame.Instance.Transition.FadeIn(FadeDurationMs / 1000f, "res://materials/transitions/fade_transition_mat.tres", null);
        }
    }

    private static async Task<bool> WaitForPlayablePlayerTurnAsync(RunState runState, int targetRound)
    {
        const int maxFrames = 300;

        for (var frame = 0; frame < maxFrames; frame++)
        {
            if (IsAtPlayablePlayerTurn(targetRound))
                return true;

            if (ShouldRefreshPlayableCombatUi(targetRound))
            {
                TryRefreshLocalCombatUi(runState, targetRound, $"frame-{frame}");
                if (IsAtPlayablePlayerTurn(targetRound))
                    return true;
            }

            if (frame == 0 || (frame + 1) % 60 == 0)
                LogCurrentCombatSummary($"waiting-playable:round-{targetRound}:frame-{frame}");

            await Engine.GetMainLoop().ToSignal(Engine.GetMainLoop(), SceneTree.SignalName.ProcessFrame);
        }

        MainFile.Logger.Warn($"Timed out while waiting for previous-step replay restore to become playable for round {targetRound}.");
        LogCurrentCombatSummary($"restore-timeout:round-{targetRound}");
        return false;
    }

    private static bool ShouldRefreshPlayableCombatUi(int targetRound)
    {
        var combatState = CombatManager.Instance.DebugOnlyGetState();
        return combatState != null
               && combatState.RoundNumber == targetRound
               && combatState.CurrentSide == MegaCrit.Sts2.Core.Combat.CombatSide.Player
               && CombatManager.Instance.IsInProgress
               && RunManager.Instance.ActionQueueSynchronizer.CombatState == ActionSynchronizerCombatState.PlayPhase;
    }

    private static void TryRefreshLocalCombatUi(RunState runState, int targetRound, string refreshReason)
    {
        var combatState = CombatManager.Instance.DebugOnlyGetState();
        if (combatState == null || combatState.RoundNumber != targetRound)
            return;

        try
        {
            CombatManager.Instance.StateTracker.SetState(combatState);
            RebuildLocalHandUi(runState);

            var handUi = NPlayerHand.Instance;
            if (handUi != null)
                HandOnCombatStateChangedMethod?.Invoke(handUi, [combatState]);

            var endTurnButton = NCombatRoom.Instance?.Ui?.EndTurnButton;
            if (endTurnButton != null && combatState.CurrentSide == CombatSide.Player)
                EndTurnButtonOnTurnStartedMethod?.Invoke(endTurnButton, [combatState]);
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"Failed to refresh singleplayer previous-step combat UI on {refreshReason}: {e.Message}");
        }
    }

    private static void RebuildLocalHandUi(RunState runState)
    {
        var handUi = NPlayerHand.Instance;
        var localPlayer = LocalContext.GetMe(runState);
        if (handUi == null || localPlayer?.PlayerCombatState == null)
            return;

        handUi.CancelAllCardPlay();

        foreach (var holder in handUi.ActiveHolders.ToList())
        {
            var cardModel = holder.CardModel;
            if (cardModel != null)
                handUi.Remove(cardModel);
        }

        var handPile = CardPile.Get(PileType.Hand, localPlayer);
        if (handPile == null)
            return;

        for (var index = 0; index < handPile.Cards.Count; index++)
        {
            var card = handPile.Cards[index];
            var cardNode = NCard.Create(card, ModelVisibility.Visible);
            if (cardNode == null)
                continue;

            handUi.Add(cardNode, index);
        }

        handUi.ForceRefreshCardIndices();
    }

    private static bool? ReadBooleanMember(object? value)
    {
        if (value is bool boolValue)
            return boolValue;

        return null;
    }

    private static bool? ReadBooleanField(FieldInfo? fieldInfo, object? instance)
    {
        if (fieldInfo == null || instance == null)
            return null;

        return ReadBooleanMember(fieldInfo.GetValue(instance));
    }

    private static bool? InvokeBooleanMethod(MethodInfo? methodInfo, object? instance)
    {
        if (methodInfo == null || instance == null)
            return null;

        return ReadBooleanMember(methodInfo.Invoke(instance, []));
    }

    private static bool? ReadBooleanProperty(PropertyInfo? propertyInfo, object? instance)
    {
        if (propertyInfo == null || instance == null)
            return null;

        return ReadBooleanMember(propertyInfo.GetValue(instance));
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

    private static bool? GetHandCanPlayCardsState()
    {
        return InvokeBooleanMethod(HandCanPlayCardsMethod, NPlayerHand.Instance);
    }

    private static bool? GetHandActionsAllowedState()
    {
        return InvokeBooleanMethod(HandAreCardActionsAllowedMethod, NPlayerHand.Instance);
    }

    private static bool? GetEndTurnCanBeEndedState()
    {
        return ReadBooleanProperty(EndTurnCanBeEndedProperty, NCombatRoom.Instance?.Ui?.EndTurnButton);
    }

    private static string GetEndTurnStateText()
    {
        return EndTurnStateField?.GetValue(NCombatRoom.Instance?.Ui?.EndTurnButton)?.ToString() ?? "null";
    }

    private static bool IsLocalPlayerActionStatePlayable()
    {
        return !CombatManager.Instance.PlayerActionsDisabled
               && IsFalseOrMissing(GetHandDisabledState())
               && IsTrueOrMissing(GetHandActionsAllowedState());
    }

    private static bool IsAtCorePlayablePlayerTurn(int targetRound)
    {
        var combatState = CombatManager.Instance.DebugOnlyGetState();
        return combatState != null
               && combatState.RoundNumber == targetRound
               && combatState.CurrentSide == MegaCrit.Sts2.Core.Combat.CombatSide.Player
               && CombatManager.Instance.IsInProgress
               && CombatManager.Instance.IsPlayPhase
               && !CombatManager.Instance.EndingPlayerTurnPhaseOne
               && !CombatManager.Instance.EndingPlayerTurnPhaseTwo
               && !RunManager.Instance.ActionExecutor.IsPaused
               && RunManager.Instance.ActionQueueSynchronizer.CombatState == ActionSynchronizerCombatState.PlayPhase;
    }

    private static bool IsAtPlayablePlayerTurn(int targetRound)
    {
        return IsAtCorePlayablePlayerTurn(targetRound) && IsLocalPlayerActionStatePlayable();
    }

    private static void LogCurrentCombatSummary(string label)
    {
        try
        {
            var combatState = CombatManager.Instance.DebugOnlyGetState();
            if (combatState == null)
            {
                MainFile.Logger.Info($"[prev-step:{label}] combat state is null.");
                return;
            }

            var localPlayer = LocalContext.GetMe(combatState);
            var hand = localPlayer?.PlayerCombatState?.Hand?.Cards
                .Select(card =>
                {
                    var canPlay = card.CanPlay(out var reason, out var preventer);
                    var preventerText = preventer != null ? preventer.Id.Entry : "null";
                    return $"{card.Id.Entry}(play={canPlay},reason={reason},preventer={preventerText})";
                })
                .ToList() ?? [];
            var enemies = combatState.Enemies
                .Select(enemy => $"{enemy.Monster?.Id.Entry ?? "unknown"} hp={enemy.CurrentHp}/{enemy.MaxHp} block={enemy.Block} move={enemy.Monster?.NextMove?.Id ?? "null"}")
                .ToList();
            var handDisabled = GetHandDisabledState();
            var netType = RunManager.Instance.NetService?.Type.ToString() ?? "null";
            var netServiceImpl = RunManager.Instance.NetService?.GetType().Name ?? "null";
            var isSinglePlayer = RunManager.Instance.IsSinglePlayerOrFakeMultiplayer;
            var handCanPlayCards = GetHandCanPlayCardsState();
            var handActionsAllowed = GetHandActionsAllowedState();
            var endTurnState = GetEndTurnStateText();
            var endTurnCanBeEnded = GetEndTurnCanBeEndedState();
            var extraTurnPlayers = string.Join(",", CombatManager.Instance.PlayersTakingExtraTurn.Select(player => player.NetId));

            MainFile.Logger.Info(
                $"[prev-step:{label}] round={combatState.RoundNumber} side={combatState.CurrentSide} " +
                $"playPhase={CombatManager.Instance.IsPlayPhase} paused={RunManager.Instance.ActionExecutor.IsPaused} " +
                $"syncState={RunManager.Instance.ActionQueueSynchronizer.CombatState} netType={netType} netImpl={netServiceImpl} isSinglePlayer={isSinglePlayer} " +
                $"playerActionsDisabled={CombatManager.Instance.PlayerActionsDisabled} " +
                $"handDisabled={(handDisabled.HasValue ? handDisabled.Value : false)} handCanPlayCards={(handCanPlayCards.HasValue ? handCanPlayCards.Value : false)} " +
                $"handActionsAllowed={(handActionsAllowed.HasValue ? handActionsAllowed.Value : false)} " +
                $"endTurnState={endTurnState} endTurnCanBeEnded={(endTurnCanBeEnded.HasValue ? endTurnCanBeEnded.Value : false)} " +
                $"extraTurnPlayers=[{extraTurnPlayers}] energy={localPlayer?.PlayerCombatState?.Energy} stars={localPlayer?.PlayerCombatState?.Stars}");
            MainFile.Logger.Info($"[prev-step:{label}] hand={string.Join(", ", hand)}");
            MainFile.Logger.Info($"[prev-step:{label}] enemies={string.Join(" | ", enemies)}");
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"Failed to log previous-step combat summary for {label}: {e.Message}");
        }
    }

    private static void LogExpectedMarkerState(CombatReplay replay, int targetRound)
    {
        try
        {
            if (replay.checksumData.Count == 0)
            {
                MainFile.Logger.Info($"[prev-step:expected:round-{targetRound}] no checksum data available.");
                return;
            }

            var expected = replay.checksumData[^1];
            var playerState = expected.fullState.Players.FirstOrDefault();
            var enemies = expected.fullState.Creatures
                .Where(creature => creature.playerId == null)
                .Select(creature =>
                {
                    var powerSummary = creature.powers == null
                        ? "none"
                        : string.Join(",", creature.powers.Select(power => $"{power.id.Entry}:{power.amount}"));
                    return $"{creature.monsterId?.Entry ?? "unknown"} hp={creature.currentHp}/{creature.maxHp} block={creature.block} powers=[{powerSummary}]";
                })
                .ToList();
            var hand = playerState.piles?
                .Where(pile => pile.pileType == PileType.Hand)
                .SelectMany(pile => pile.cards)
                .Select(card => card.card.Id.Entry)
                .ToList() ?? [];

            MainFile.Logger.Info(
                $"[prev-step:expected:round-{targetRound}] checksumContext={expected.context} " +
                $"energy={playerState.energy} stars={playerState.stars} gold={playerState.gold} hand=[{string.Join(", ", hand)}]");
            MainFile.Logger.Info($"[prev-step:expected:round-{targetRound}] enemies={string.Join(" | ", enemies)}");
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"Failed to log expected previous-step marker state for round {targetRound}: {e.Message}");
        }
    }

    private static void SaveSession(ReplaySessionMetadata metadata, CombatReplay replay)
    {
        var sessionDirectory = ProjectSettings.GlobalizePath(GetSessionDirectoryPath());
        Directory.CreateDirectory(sessionDirectory);

        var metadataJson = JsonSerializer.Serialize(metadata, JsonOptions);
        File.WriteAllText(Path.Combine(sessionDirectory, MetadataFileName), metadataJson, Encoding.UTF8);

        var writer = new PacketWriter();
        writer.Write(replay);
        writer.ZeroByteRemainder();
        File.WriteAllBytes(Path.Combine(sessionDirectory, ReplayFileName), writer.Buffer.AsSpan(0, writer.BytePosition).ToArray());
    }

    private static ReplaySessionMetadata? LoadMetadata()
    {
        var metadataPath = ProjectSettings.GlobalizePath(Path.Combine(GetSessionDirectoryPath(), MetadataFileName));
        if (!File.Exists(metadataPath))
            return null;

        try
        {
            return JsonSerializer.Deserialize<ReplaySessionMetadata>(File.ReadAllText(metadataPath, Encoding.UTF8), JsonOptions);
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"Failed to parse previous-step replay session metadata: {e.Message}");
            return null;
        }
    }

    private static CombatReplay? LoadReplay()
    {
        var replayPath = ProjectSettings.GlobalizePath(Path.Combine(GetSessionDirectoryPath(), ReplayFileName));
        if (!File.Exists(replayPath))
            return null;

        try
        {
            var reader = new PacketReader();
            reader.Reset(File.ReadAllBytes(replayPath));
            return reader.Read<CombatReplay>();
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"Failed to parse previous-step replay session: {e.Message}");
            return null;
        }
    }

    private static void ClearLegacySnapshotArtifacts()
    {
        DeleteProfileScopedDirectory(LegacySnapshotDirectoryName);
        DeleteProfileScopedDirectory(LegacyNonStandardSnapshotDirectoryName);
    }

    private static void DeleteProfileScopedDirectory(string relativeDirectory)
    {
        var path = ProjectSettings.GlobalizePath(
            UserDataPathProvider.GetProfileScopedPath(
                SaveManager.Instance.CurrentProfileId,
                Path.Combine(UserDataPathProvider.SavesDir, relativeDirectory)));

        if (Directory.Exists(path))
            Directory.Delete(path, true);
    }

    private static string GetSessionDirectoryPath()
    {
        return UserDataPathProvider.GetProfileScopedPath(
            SaveManager.Instance.CurrentProfileId,
            Path.Combine(UserDataPathProvider.SavesDir, SessionDirectoryName));
    }

    private static string SanitizeFilePart(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray());
        return sanitized.Replace(':', '_');
    }

    private static bool AreEquivalent(ReplayTurnMarker? left, ReplayTurnMarker? right)
    {
        return left != null
               && right != null
               && left.RoundNumber == right.RoundNumber
               && left.RoomScopeKey == right.RoomScopeKey
               && left.EventCount == right.EventCount
               && left.ChecksumCount == right.ChecksumCount
               && left.NextActionId == right.NextActionId
               && left.NextHookId == right.NextHookId
               && left.NextChecksumId == right.NextChecksumId
               && left.ChoiceIds.SequenceEqual(right.ChoiceIds);
    }

    private sealed class ReplaySessionMetadata
    {
        public string RoomScopeKey { get; set; } = string.Empty;
        public List<ReplayTurnMarker> Markers { get; set; } = [];
    }

    private sealed class ReplayTurnMarker
    {
        public int RoundNumber { get; set; }
        public string RoomScopeKey { get; set; } = string.Empty;
        public int EventCount { get; set; }
        public int ChecksumCount { get; set; }
        public uint NextActionId { get; set; }
        public uint NextHookId { get; set; }
        public uint NextChecksumId { get; set; }
        public List<uint> ChoiceIds { get; set; } = [];
    }
}
