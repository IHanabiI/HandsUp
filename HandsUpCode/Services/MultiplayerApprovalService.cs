using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;
using HandsUp.HandsUpCode.Multiplayer;
using HandsUp.HandsUpCode.Multiplayer.Messages;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Platform.Steam;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using Steamworks;
using HandsUp.HandsUpCode.UI;

namespace HandsUp.HandsUpCode.Services;

public static class MultiplayerApprovalService
{
    private static readonly MessageHandlerDelegate<RaiseHandApprovalRequestMessage> RequestHandler = HandleRequestMessage;
    private static readonly MessageHandlerDelegate<RaiseHandApprovalResponseMessage> ResponseHandler = HandleResponseMessage;
    private static readonly MessageHandlerDelegate<RaiseHandExecuteMessage> ExecuteHandler = HandleExecuteMessage;

    private static INetGameService? _registeredNetService;
    private static RunState? _runState;
    private static PendingApproval? _pendingApproval;
    private static IncomingApproval? _incomingApproval;
    private static bool _incomingApprovalPresentationScheduled;
    private static Func<RaiseHandActionKind, string?, string?, string?, string?, string?, System.Threading.Tasks.Task>? _executeApprovedActionAsync;

    public static void RegisterIfNeeded(INetGameService? netService, RunState? runState)
    {
        if (netService == null || runState == null)
            return;

        if (_registeredNetService == netService)
        {
            _runState = runState;
            return;
        }

        Unregister();
        _registeredNetService = netService;
        _runState = runState;
        _registeredNetService.RegisterMessageHandler(RequestHandler);
        _registeredNetService.RegisterMessageHandler(ResponseHandler);
        _registeredNetService.RegisterMessageHandler(ExecuteHandler);
        MainFile.Logger.Info("Registered HandsUp multiplayer approval handlers.");
    }

    public static void Unregister()
    {
        if (_registeredNetService != null)
        {
            _registeredNetService.UnregisterMessageHandler(RequestHandler);
            _registeredNetService.UnregisterMessageHandler(ResponseHandler);
            _registeredNetService.UnregisterMessageHandler(ExecuteHandler);
        }

        _registeredNetService = null;
        _runState = null;
        _pendingApproval = null;
        _incomingApproval = null;
        _incomingApprovalPresentationScheduled = false;
        MultiplayerExecutionWindowService.Clear("multiplayer approval service unregistered");
    }

    public static bool IsMultiplayerActive()
    {
        return RunManager.Instance.RunLobby != null && _registeredNetService != null;
    }

    public static void RegisterActionExecutor(Func<RaiseHandActionKind, string?, string?, string?, string?, string?, System.Threading.Tasks.Task> executor)
    {
        _executeApprovedActionAsync = executor;
    }

    public static bool TryBeginApproval(RaiseHandActionKind actionKind, out string statusMessage)
    {
        statusMessage = string.Empty;

        if (!IsMultiplayerActive() || _runState == null || _registeredNetService == null)
            return false;

        if (_pendingApproval != null)
        {
            statusMessage = "\u5f53\u524d\u5df2\u6709\u4e00\u4e2a\u8054\u673a\u7533\u8bf7\u5728\u7b49\u5f85\u961f\u53cb\u56de\u590d\u3002";
            return true;
        }

        var me = LocalContext.GetMe(_runState);
        if (me == null)
        {
            statusMessage = "\u8054\u673a\u7533\u8bf7\u5931\u8d25\uff1a\u672a\u627e\u5230\u672c\u5730\u73a9\u5bb6\u4fe1\u606f\u3002";
            return true;
        }

        var teammateIds = _runState.Players
            .Where(player => !LocalContext.IsMe(player))
            .Select(player => player.NetId)
            .ToList();

        if (teammateIds.Count == 0)
        {
            statusMessage = "\u5f53\u524d\u8054\u673a\u5bf9\u5c40\u4e2d\u6ca1\u6709\u5176\u4ed6\u961f\u53cb\u3002";
            return true;
        }

        var evaluationState = RunManager.Instance.DebugOnlyGetState() ?? _runState;
        var localDecision = MultiplayerApprovalPrecheckService.Evaluate(actionKind, evaluationState);
        if (!localDecision.CanProceed)
        {
            statusMessage = localDecision.StatusText;
            if (localDecision.ShouldShowPopup)
                ShowInfoPopup(localDecision.PopupTitle, localDecision.PopupBody);
            return true;
        }

        var requestId = Guid.NewGuid().ToString("N");
        var initiatorLabel = GetPlayerLabel(me.NetId);
        var message = new RaiseHandApprovalRequestMessage
        {
            RequestId = requestId,
            InitiatorNetId = me.NetId,
            ActionKind = actionKind,
            InitiatorLabel = initiatorLabel
        };

        try
        {
            _pendingApproval = new PendingApproval(
                requestId,
                actionKind,
                initiatorLabel,
                teammateIds,
                ApprovalRequestState.Capture(actionKind, evaluationState));

            foreach (var teammateId in teammateIds)
            {
                MainFile.Logger.Info(
                    $"Sending HandsUp approval request {requestId} to teammate {teammateId} for action {RaiseHandActionService.GetActionLabel(actionKind)}.");
                _registeredNetService.SendMessage(message, teammateId);
            }
        }
        catch (Exception e)
        {
            _pendingApproval = null;
            MainFile.Logger.Error($"Failed to send HandsUp approval request: {e}");
            statusMessage = "\u8054\u673a\u7533\u8bf7\u53d1\u9001\u5931\u8d25\uff0c\u8bf7\u5148\u67e5\u770b\u65e5\u5fd7\u3002";
            return true;
        }

        statusMessage = "\u8054\u673a\u7533\u8bf7\u5df2\u53d1\u51fa\uff0c\u6b63\u5728\u7b49\u5f85\u961f\u53cb\u51b3\u5b9a\u3002";
        return true;
    }

    private static void HandleRequestMessage(RaiseHandApprovalRequestMessage message, ulong senderId)
    {
        if (_registeredNetService == null || _runState == null)
            return;

        if (senderId == _registeredNetService.NetId)
            return;

        MainFile.Logger.Info(
            $"Received HandsUp approval request {message.RequestId} from {senderId} for action {RaiseHandActionService.GetActionLabel(message.ActionKind)}.");
        EnqueueIncomingApproval(message, senderId);
    }

    private static void HandleResponseMessage(RaiseHandApprovalResponseMessage message, ulong senderId)
    {
        if (_pendingApproval == null || _registeredNetService == null)
            return;

        if (_pendingApproval.RequestId != message.RequestId)
            return;

        MainFile.Logger.Info($"Received HandsUp approval response {message.RequestId} from {senderId}: approved={message.Approved}.");
        _pendingApproval.Responses[senderId] = message.Approved;

        if (!message.Approved)
        {
            ShowInfoPopup("\u795e\u79d8\u529b\u91cf\u6d88\u6563", "[center]\u4f60\u7684\u961f\u53cb\u8f7b\u8f7b\u4e00\u7b11[/center]\n[center]\u8868\u793a\u6211\u770b\u672a\u5fc5[/center]\n[center]\u5e76\u5632\u7b11\u4e86\u4f60[/center]\n[center]\u795e\u79d8\u529b\u91cf\u6d88\u6563\u4e86[/center]");
            _pendingApproval = null;
            return;
        }

        if (_pendingApproval.ExpectedResponderIds.All(id => _pendingApproval.Responses.ContainsKey(id)))
        {
            if (TryBuildPendingExecuteMessage(_pendingApproval, out var executeMessage))
                BroadcastExecuteMessage(executeMessage);
            _pendingApproval = null;
        }
    }

    private static void HandleExecuteMessage(RaiseHandExecuteMessage message, ulong senderId)
    {
        if (_executeApprovedActionAsync == null || _registeredNetService == null)
            return;

        if (senderId == _registeredNetService.NetId)
            return;

        MainFile.Logger.Info(
            $"Received HandsUp execute message from {senderId} for action {RaiseHandActionService.GetActionLabel(message.ActionKind)}.");
        MultiplayerExecutionWindowService.BeginExecutionWindow(
            $"received execute for {RaiseHandActionService.GetActionLabel(message.ActionKind)} from {senderId}");
        TaskHelper.RunSafely(_executeApprovedActionAsync(message.ActionKind, message.RunJson, message.RoomJson, message.SourceRoomType, message.CombatStateJson, message.RestoreHint));
    }

    private static void EnqueueIncomingApproval(RaiseHandApprovalRequestMessage message, ulong senderId)
    {
        var nextApproval = new IncomingApproval(message.RequestId, senderId, message.ActionKind, message.InitiatorLabel);
        var previousApproval = _incomingApproval;
        if (previousApproval != null && previousApproval.RequestId != nextApproval.RequestId)
        {
            MainFile.Logger.Info($"Superseding HandsUp approval request {previousApproval.RequestId} with newer request {nextApproval.RequestId}.");
            SendApprovalResponse(previousApproval.RequestId, previousApproval.InitiatorId, false);
        }

        _incomingApproval = nextApproval;
        ShowApprovalPopup();
    }

    private static void ShowApprovalPopup()
    {
        if (NModalContainer.Instance == null)
            return;

        if (_incomingApproval == null)
            return;

        if (NModalContainer.Instance.OpenModal != null)
        {
            MainFile.Logger.Info("HandsUp approval popup is preempting the current modal because approval requests have highest priority.");
            NModalContainer.Instance.Clear();
            ScheduleIncomingApprovalPresentation();
            return;
        }

        _incomingApprovalPresentationScheduled = false;

        var popup = NDisconnectConfirmPopup.Create();
        if (popup == null)
            return;

        NModalContainer.Instance.Add(popup, true);
        Callable.From(() => ConfigureApprovalPopup(popup)).CallDeferred();
    }

    private static void ScheduleIncomingApprovalPresentation()
    {
        if (_incomingApprovalPresentationScheduled)
            return;

        _incomingApprovalPresentationScheduled = true;
        Callable.From(ShowApprovalPopup).CallDeferred();
    }

    private static void ConfigureApprovalPopup(NDisconnectConfirmPopup popup)
    {
        if (_incomingApproval == null)
        {
            NModalContainer.Instance?.Clear();
            return;
        }

        var verticalPopup = popup.GetNodeOrNull<NVerticalPopup>("VerticalPopup");
        if (verticalPopup == null)
        {
            NModalContainer.Instance?.Clear();
            return;
        }

        verticalPopup.DisconnectSignals();
        verticalPopup.DisconnectHotkeys();
        verticalPopup.SetText(
            RaiseHandActionService.GetConfirmPopupTitle(_incomingApproval.ActionKind),
            BuildApprovalBody(_incomingApproval));
        verticalPopup.YesButton.IsYes = true;
        verticalPopup.NoButton.IsYes = false;
        verticalPopup.InitYesButton(new LocString("main_menu_ui", "GENERIC_POPUP.confirm"), _ => RespondToIncomingApproval(true));
        verticalPopup.InitNoButton(new LocString("main_menu_ui", "GENERIC_POPUP.cancel"), _ => RespondToIncomingApproval(false));
        verticalPopup.YesButton.SetText("\u770b\u4ed6\u8868\u6f14");
        verticalPopup.NoButton.SetText("\u6211\u770b\u672a\u5fc5");
    }

    private static void RespondToIncomingApproval(bool approved)
    {
        var incomingApproval = _incomingApproval;
        if (incomingApproval == null)
            return;

        _incomingApproval = null;
        SendApprovalResponse(incomingApproval.RequestId, incomingApproval.InitiatorId, approved);
    }

    private static string BuildApprovalBody(IncomingApproval message)
    {
        return
            $"[center]{message.InitiatorLabel}\u4e3e\u624b\u53eb\u505c\u4e86\u6bd4\u8d5b[/center]\n" +
            "[center]\u7533\u8bf7\u4f7f\u7528\u795e\u79d8\u529b\u91cf[/center]\n" +
            $"{RaiseHandActionService.GetConfirmPopupBody(message.ActionKind)}\n" +
            "[center]\u4f60\u51b3\u5b9a\uff1a[/center]";
    }

    private static void SendApprovalResponse(string requestId, ulong initiatorId, bool approved)
    {
        if (_registeredNetService == null)
            return;

        var response = new RaiseHandApprovalResponseMessage
        {
            RequestId = requestId,
            Approved = approved
        };

        MainFile.Logger.Info($"Sending HandsUp approval response {requestId} to initiator {initiatorId}: approved={approved}.");
        _registeredNetService.SendMessage(response, initiatorId);
    }

    private static bool TryBuildPendingExecuteMessage(PendingApproval pendingApproval, out RaiseHandExecuteMessage executeMessage)
    {
        executeMessage = default;

        var currentState = RunManager.Instance.DebugOnlyGetState() ?? _runState;
        if (!pendingApproval.RequestState.MatchesCurrent(pendingApproval.ActionKind, currentState))
        {
            var stateChangedDecision = MultiplayerApprovalPrecheckService.StateChanged();
            MainFile.Logger.Info(
                $"Discarding HandsUp approval request {pendingApproval.RequestId} for {RaiseHandActionService.GetActionLabel(pendingApproval.ActionKind)} because the local state changed before execution.");
            ShowInfoPopup(stateChangedDecision.PopupTitle, stateChangedDecision.PopupBody);
            return false;
        }

        var builtMessage = BuildExecuteMessage(pendingApproval.ActionKind);
        if (builtMessage == null)
        {
            MainFile.Logger.Info(
                $"Skipped broadcasting HandsUp execute message for {RaiseHandActionService.GetActionLabel(pendingApproval.ActionKind)} because no execute payload was produced.");
            return false;
        }

        executeMessage = builtMessage.Value;
        return true;
    }

    private static void BroadcastExecuteMessage(RaiseHandExecuteMessage executeMessage)
    {
        if (_registeredNetService == null || _executeApprovedActionAsync == null)
            return;

        var actionLabel = RaiseHandActionService.GetActionLabel(executeMessage.ActionKind);
        MultiplayerExecutionWindowService.BeginExecutionWindow($"broadcasting local execute for {actionLabel}");

        MainFile.Logger.Info(
            $"Broadcasting HandsUp execute message for action {actionLabel}.");

        try
        {
            _registeredNetService.SendMessage(executeMessage);
            TaskHelper.RunSafely(ExecuteLocalApprovedActionAsync(executeMessage));
        }
        catch
        {
            MultiplayerExecutionWindowService.CancelPendingExecutionWindow(
                $"broadcast send failed for {actionLabel}");
            throw;
        }
    }

    private static async System.Threading.Tasks.Task ExecuteLocalApprovedActionAsync(RaiseHandExecuteMessage executeMessage)
    {
        if (_executeApprovedActionAsync == null || _registeredNetService == null)
            return;

        if (_registeredNetService.Type == NetGameType.Host
            && ShouldDelayHostLocalReload(executeMessage))
        {
            MainFile.Logger.Info(
                $"Delaying host local multiplayer reload briefly for {RaiseHandActionService.GetActionLabel(executeMessage.ActionKind)} " +
                "so remote clients can enter reload before the host re-enters the restored state.");
            await System.Threading.Tasks.Task.Delay(250);
        }

        await _executeApprovedActionAsync(
            executeMessage.ActionKind,
            executeMessage.RunJson,
            executeMessage.RoomJson,
            executeMessage.SourceRoomType,
            executeMessage.CombatStateJson,
            executeMessage.RestoreHint);
    }

    private static bool ShouldDelayHostLocalReload(RaiseHandExecuteMessage executeMessage)
    {
        if (executeMessage.ActionKind == RaiseHandActionKind.SoftRestart)
            return true;

        return executeMessage.ActionKind == RaiseHandActionKind.PreviousStep
               && string.IsNullOrWhiteSpace(executeMessage.RoomJson);
    }

    private static RaiseHandExecuteMessage? BuildExecuteMessage(RaiseHandActionKind actionKind)
    {
        try
        {
            var evaluationState = RunManager.Instance.DebugOnlyGetState() ?? _runState;
            var localDecision = MultiplayerApprovalPrecheckService.Evaluate(actionKind, evaluationState);
            if (!localDecision.CanProceed)
            {
                if (localDecision.ShouldShowPopup)
                    ShowInfoPopup(localDecision.PopupTitle, localDecision.PopupBody);
                else
                    MainFile.Logger.Warn($"Skipped building multiplayer execute message: {localDecision.StatusText}");
                return null;
            }

            return actionKind switch
            {
                RaiseHandActionKind.Restart => BuildRestartExecuteMessage(),
                RaiseHandActionKind.SoftRestart => BuildSoftRestartExecuteMessage(actionKind),
                RaiseHandActionKind.PreviousStep => BuildPreviousStepExecuteMessage(),
                RaiseHandActionKind.PreviousFloor => BuildPreviousFloorExecuteMessage(),
                _ => null
            };
        }
        catch (Exception e)
        {
            MainFile.Logger.Error(
                $"Failed to build execute message for {RaiseHandActionService.GetActionLabel(actionKind)}: {e}");
            return null;
        }
    }

    private static RaiseHandExecuteMessage? BuildRestartExecuteMessage()
    {
        var runState = RunManager.Instance.DebugOnlyGetState();
        if (runState == null)
            return null;

        var restartSnapshot = RunManager.Instance.ToSave(null);
        if (restartSnapshot.SerializableRng != null && restartSnapshot.DailyTime == null)
            restartSnapshot.SerializableRng.Seed = SeedHelper.GetRandomSeed(runState.Rng.Niche, 10);

        return new RaiseHandExecuteMessage
        {
            ActionKind = RaiseHandActionKind.Restart,
            RunJson = SaveManager.ToJson(restartSnapshot),
            RoomJson = string.Empty,
            SourceRoomType = runState.CurrentRoom?.RoomType.ToString() ?? string.Empty,
            CombatStateJson = string.Empty
        };
    }

    private static RaiseHandExecuteMessage? BuildSoftRestartExecuteMessage(RaiseHandActionKind actionKind, string? restoreHintOverride = null)
    {
        var runState = RunManager.Instance.DebugOnlyGetState();
        if (runState == null)
            return null;

        if (MultiplayerRunStartStageService.IsAtNeowStage(runState))
        {
            MainFile.Logger.Info("Routing multiplayer soft restart through the Neow run-start restore payload.");
            return BuildNeowRunStartExecuteMessage(actionKind, runState);
        }

        if (runState.CurrentRoom?.RoomType == RoomType.Shop)
        {
            var shopSnapshot = MultiplayerSoftRestartSnapshotService.TryReadShopPreEntrySnapshotForCurrentShop(runState);
            if (shopSnapshot != null && !string.IsNullOrWhiteSpace(shopSnapshot.RunJson))
            {
                MainFile.Logger.Info("Using multiplayer shop pre-entry snapshot as the primary payload for shop soft restart.");
                return new RaiseHandExecuteMessage
                {
                    ActionKind = actionKind,
                    RunJson = shopSnapshot.RunJson,
                    RoomJson = string.Empty,
                    SourceRoomType = string.IsNullOrWhiteSpace(shopSnapshot.SourceRoomType)
                        ? runState.CurrentRoom.RoomType.ToString()
                        : shopSnapshot.SourceRoomType,
                    CombatStateJson = string.Empty,
                    RestoreHint = restoreHintOverride ?? "shop_pre_entry_snapshot"
                };
            }

            MainFile.Logger.Warn("Multiplayer shop pre-entry snapshot was unavailable for the current shop room; falling back to the generic multiplayer soft restart payload selection.");
        }

        var shouldIncludeInitialPileSnapshot = MultiplayerSoftRestartRoomClassifier.ShouldUseInitialCombatPileSnapshot(runState);
        var shouldPreferRoomEntrySnapshot = shouldIncludeInitialPileSnapshot
                                            || MultiplayerSoftRestartRoomClassifier.IsEventScopedRoom(runState);
        var localPlayerId = LocalContext.GetMe(runState)?.NetId ?? LocalContext.NetId ?? 0UL;
        var readResult = localPlayerId != 0UL
            ? SaveManager.Instance.LoadAndCanonicalizeMultiplayerRunSave(localPlayerId)
            : null;
        var initialPileSnapshotJson = shouldIncludeInitialPileSnapshot
            ? MultiplayerInitialCombatPileSnapshotService.TryReadSnapshotJsonForCurrentRoom(RunManager.Instance)
            : null;

        if (!shouldIncludeInitialPileSnapshot && MultiplayerSoftRestartRoomClassifier.IsEventScopedRoom(runState))
        {
            var roomKind = MultiplayerSoftRestartRoomClassifier.IsAncientRoom(runState)
                ? "ancient"
                : "event-scoped";
            MainFile.Logger.Info($"Treating current multiplayer soft restart as {roomKind}, so the initial combat pile snapshot is intentionally skipped.");
        }

        var multiplayerSnapshot = MultiplayerSoftRestartSnapshotService.TryReadSnapshot();
        if (shouldPreferRoomEntrySnapshot
            && multiplayerSnapshot != null
            && !string.IsNullOrWhiteSpace(multiplayerSnapshot.RunJson))
        {
            MainFile.Logger.Info("Using multiplayer room-entry snapshot as the primary payload for combat soft restart.");
            return new RaiseHandExecuteMessage
            {
                ActionKind = actionKind,
                RunJson = multiplayerSnapshot.RunJson,
                RoomJson = string.Empty,
                SourceRoomType = string.IsNullOrWhiteSpace(multiplayerSnapshot.SourceRoomType)
                    ? runState.CurrentRoom?.RoomType.ToString() ?? string.Empty
                    : multiplayerSnapshot.SourceRoomType,
                CombatStateJson = initialPileSnapshotJson ?? string.Empty,
                RestoreHint = restoreHintOverride ?? "room_entry_snapshot"
            };
        }

        if (readResult != null && readResult.Success && readResult.SaveData != null)
        {
            MainFile.Logger.Info("Using canonicalized current_run_mp.save as the primary payload for multiplayer soft restart.");
            return new RaiseHandExecuteMessage
            {
                ActionKind = actionKind,
                RunJson = SaveManager.ToJson(readResult.SaveData),
                RoomJson = string.Empty,
                SourceRoomType = runState.CurrentRoom?.RoomType.ToString() ?? string.Empty,
                CombatStateJson = initialPileSnapshotJson ?? string.Empty,
                RestoreHint = restoreHintOverride ?? "current_run_mp_save"
            };
        }

        if (multiplayerSnapshot != null && !string.IsNullOrWhiteSpace(multiplayerSnapshot.RunJson))
        {
            MainFile.Logger.Info("Using multiplayer room-entry snapshot as fallback for multiplayer soft restart.");
            return new RaiseHandExecuteMessage
            {
                ActionKind = actionKind,
                RunJson = multiplayerSnapshot.RunJson,
                RoomJson = string.Empty,
                SourceRoomType = string.IsNullOrWhiteSpace(multiplayerSnapshot.SourceRoomType)
                    ? runState.CurrentRoom?.RoomType.ToString() ?? string.Empty
                    : multiplayerSnapshot.SourceRoomType,
                CombatStateJson = initialPileSnapshotJson ?? string.Empty,
                RestoreHint = restoreHintOverride ?? "room_entry_snapshot_fallback"
            };
        }

        MainFile.Logger.Warn("Falling back to in-memory run serialization for multiplayer soft restart because current_run_mp.save and multiplayer snapshot could not be loaded.");

        return new RaiseHandExecuteMessage
        {
            ActionKind = actionKind,
            RunJson = SaveManager.ToJson(RunManager.Instance.ToSave(null)),
            RoomJson = string.Empty,
            SourceRoomType = runState.CurrentRoom?.RoomType.ToString() ?? string.Empty,
            CombatStateJson = initialPileSnapshotJson ?? string.Empty,
            RestoreHint = restoreHintOverride ?? string.Empty
        };
    }

    private static RaiseHandExecuteMessage? BuildPreviousFloorExecuteMessage()
    {
        var runState = RunManager.Instance.DebugOnlyGetState();
        if (runState != null
            && (MultiplayerRunStartStageService.IsAtNeowStage(runState) || runState.TotalFloor <= 1))
        {
            ShowInfoPopup(RaiseHandPopupText.NoWayBackTitleText, RaiseHandPopupText.NoWayBackBodyText);
            return null;
        }

        if (!MultiplayerPreviousFloorSnapshotService.HasSnapshot())
        {
            ShowInfoPopup(RaiseHandPopupText.NoWayBackTitleText, RaiseHandPopupText.NoWayBackBodyText);
            return null;
        }

        var snapshotRun = MultiplayerPreviousFloorSnapshotService.PeekLatestSnapshot();
        return new RaiseHandExecuteMessage
        {
            ActionKind = RaiseHandActionKind.PreviousFloor,
            RunJson = SaveManager.ToJson(snapshotRun),
            RoomJson = string.Empty,
            SourceRoomType = string.Empty,
            CombatStateJson = string.Empty
        };
    }

    private static RaiseHandExecuteMessage? BuildPreviousStepExecuteMessage()
    {
        var runState = RunManager.Instance.DebugOnlyGetState();
        var decision = MultiplayerPreviousStepDecisionService.Evaluate(runState);
        if (!decision.CanProceed)
        {
            if (decision.ShouldShowPopup)
                ShowInfoPopup(decision.PopupTitle, decision.PopupBody);
            else
                MainFile.Logger.Warn($"Skipped building multiplayer previous-step execute message: {decision.StatusText}");
            return null;
        }

        var combatRoom = runState?.CurrentRoom as CombatRoom;
        if (combatRoom != null)
        {
            if (decision.Mode == MultiplayerPreviousStepDecisionService.RestoreMode.RoomStart)
                MultiplayerBossElitePreviousStepService.LogRoomStartRestoreRequested(decision.RoomKind, combatRoom, decision.CurrentRound);
            else
                MultiplayerBossElitePreviousStepService.LogRestoreRequested(decision.RoomKind, combatRoom, decision.CurrentRound, decision.TargetRound);
        }

        if (decision.Mode == MultiplayerPreviousStepDecisionService.RestoreMode.RoomStart)
        {
            MainFile.Logger.Info(
                $"Using multiplayer room-start restore as the previous-step payload. roomKind={decision.RoomKind} currentRound={decision.CurrentRound}.");
            return BuildSoftRestartExecuteMessage(RaiseHandActionKind.PreviousStep, decision.RestoreHint);
        }

        var snapshot = MultiplayerPreviousStepSnapshotService.RestoreSnapshotForRound(decision.TargetRound);
        MainFile.Logger.Info(
            $"Using multiplayer previous-step snapshot for round {decision.TargetRound}. roomKind={decision.RoomKind} restoreHint={decision.RestoreHint}.");
        return new RaiseHandExecuteMessage
        {
            ActionKind = RaiseHandActionKind.PreviousStep,
            RunJson = SaveManager.ToJson(snapshot.RunSnapshot),
            RoomJson = JsonSerializer.Serialize(snapshot.RoomSnapshot),
            SourceRoomType = snapshot.RoomSnapshot.RoomType.ToString(),
            CombatStateJson = snapshot.CombatStateJson,
            RestoreHint = decision.RestoreHint
        };
    }

    private static RaiseHandExecuteMessage BuildNeowRunStartExecuteMessage(RaiseHandActionKind actionKind, RunState runState)
    {
        return new RaiseHandExecuteMessage
        {
            ActionKind = actionKind,
            RunJson = SaveManager.ToJson(RunManager.Instance.ToSave(null)),
            RoomJson = string.Empty,
            SourceRoomType = runState.CurrentRoom?.RoomType.ToString() ?? string.Empty,
            CombatStateJson = string.Empty,
            RestoreHint = MultiplayerRunStartStageService.NeowRunStartRestoreHint
        };
    }

    private static void ShowInfoPopup(string titleText, string bodyText)
    {
        if (NModalContainer.Instance == null)
            return;

        if (NModalContainer.Instance.OpenModal != null)
            NModalContainer.Instance.Clear();

        var popup = NDisconnectConfirmPopup.Create();
        if (popup == null)
            return;

        NModalContainer.Instance.Add(popup, true);
        Callable.From(() => ConfigureInfoPopup(popup, titleText, bodyText)).CallDeferred();
    }

    private static string GetPlayerLabel(ulong netId)
    {
        try
        {
            if (SteamInitializer.Initialized)
            {
                var steamId = new CSteamID(netId);
                var personaName = SteamFriends.GetFriendPersonaName(steamId);
                if (!string.IsNullOrWhiteSpace(personaName))
                    return personaName;
            }
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"Failed to resolve Steam persona name for {netId}: {e.Message}");
        }

        if (_runState == null)
            return "\u67d0\u4f4d";

        var index = _runState.Players.ToList().FindIndex(player => player.NetId == netId);
        if (index < 0)
            return "\u67d0\u4f4d";
        return $"{index + 1}\u53f7";
    }

    private static void ConfigureInfoPopup(NDisconnectConfirmPopup popup, string titleText, string bodyText)
    {
        var verticalPopup = popup.GetNodeOrNull<NVerticalPopup>("VerticalPopup");
        if (verticalPopup == null)
        {
            NModalContainer.Instance?.Clear();
            return;
        }

        verticalPopup.DisconnectSignals();
        verticalPopup.DisconnectHotkeys();
        verticalPopup.SetText(titleText, bodyText);
        verticalPopup.YesButton.IsYes = true;
        verticalPopup.YesButton.SetText("\u786e\u5b9a");
        verticalPopup.NoButton.Visible = false;
        verticalPopup.NoButton.FocusMode = Control.FocusModeEnum.None;
        verticalPopup.InitYesButton(new LocString("main_menu_ui", "GENERIC_POPUP.confirm"), _ => { });
    }

    private sealed class PendingApproval
    {
        public PendingApproval(
            string requestId,
            RaiseHandActionKind actionKind,
            string initiatorLabel,
            List<ulong> expectedResponderIds,
            ApprovalRequestState requestState)
        {
            RequestId = requestId;
            ActionKind = actionKind;
            InitiatorLabel = initiatorLabel;
            ExpectedResponderIds = expectedResponderIds;
            RequestState = requestState;
        }

        public string RequestId { get; }
        public RaiseHandActionKind ActionKind { get; }
        public string InitiatorLabel { get; }
        public List<ulong> ExpectedResponderIds { get; }
        public ApprovalRequestState RequestState { get; }
        public Dictionary<ulong, bool> Responses { get; } = [];
    }

    private sealed class IncomingApproval
    {
        public IncomingApproval(string requestId, ulong initiatorId, RaiseHandActionKind actionKind, string initiatorLabel)
        {
            RequestId = requestId;
            InitiatorId = initiatorId;
            ActionKind = actionKind;
            InitiatorLabel = initiatorLabel;
        }

        public string RequestId { get; }
        public ulong InitiatorId { get; }
        public RaiseHandActionKind ActionKind { get; }
        public string InitiatorLabel { get; }
    }

    private sealed class ApprovalRequestState
    {
        private ApprovalRequestState(string scopeKey, int totalFloor, int currentRoomCount, int? roundNumber)
        {
            ScopeKey = scopeKey;
            TotalFloor = totalFloor;
            CurrentRoomCount = currentRoomCount;
            RoundNumber = roundNumber;
        }

        public string ScopeKey { get; }
        public int TotalFloor { get; }
        public int CurrentRoomCount { get; }
        public int? RoundNumber { get; }

        public static ApprovalRequestState Capture(RaiseHandActionKind actionKind, RunState? runState)
        {
            var roundNumber = actionKind == RaiseHandActionKind.PreviousStep
                ? (runState?.CurrentRoom as CombatRoom)?.CombatState.RoundNumber
                : null;
            return new ApprovalRequestState(
                BuildApprovalScopeKey(runState),
                runState?.TotalFloor ?? -1,
                runState?.CurrentRoomCount ?? -1,
                roundNumber);
        }

        public bool MatchesCurrent(RaiseHandActionKind actionKind, RunState? runState)
        {
            var current = Capture(actionKind, runState);
            return string.Equals(ScopeKey, current.ScopeKey, StringComparison.Ordinal)
                   && TotalFloor == current.TotalFloor
                   && CurrentRoomCount == current.CurrentRoomCount
                   && RoundNumber == current.RoundNumber;
        }
    }

    private static string BuildApprovalScopeKey(RunState? runState)
    {
        if (runState == null)
            return string.Empty;

        var currentRoom = runState.CurrentRoom;
        var roomIdentity = currentRoom?.ModelId?.ToString() ?? currentRoom?.RoomType.ToString() ?? "none";
        var currentCoord = runState.CurrentMapCoord?.ToString() ?? "none";
        var pointType = runState.CurrentMapPointHistoryEntry?.MapPointType
                        ?? runState.CurrentMapPoint?.PointType
                        ?? MapPointType.Unassigned;

        return $"{runState.TotalFloor:D4}_{runState.CurrentRoomCount:D4}_{pointType}_{currentCoord}_{roomIdentity}";
    }
}
