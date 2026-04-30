using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using HandsUp.HandsUpCode.Multiplayer;
using HandsUp.HandsUpCode.UI;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace HandsUp.HandsUpCode.Services;

public static class RaiseHandActionService
{
    private const string RestartLabel = "\u4ece\u5934\u518d\u6765";
    private const string SoftRestartLabel = "\u8fd9\u6b21\u4e0d\u7b97";
    private const string PreviousStepLabel = "\u56de\u5230\u4e0a\u4e00\u6b65";
    private const string PreviousFloorLabel = "\u56de\u5230\u4e0a\u4e00\u5c42";
    private const string ConfirmTitleText = "\u6ce8\u5165\u795e\u79d8\u529b\u91cf";
    private const string ConfirmButtonText = "\u786e\u5b9a";
    private const string CancelButtonText = "\u53d6\u6d88";
    private const string InfoButtonText = "\u786e\u5b9a";

    private static readonly RaiseHandActionKind[] _orderedActionKinds =
    [
        RaiseHandActionKind.Restart,
        RaiseHandActionKind.SoftRestart,
        RaiseHandActionKind.PreviousStep,
        RaiseHandActionKind.PreviousFloor
    ];

    public static IReadOnlyList<RaiseHandActionKind> OrderedActionKinds => _orderedActionKinds;

    public static void BeginActionFromMenu(RaiseHandActionKind actionKind, Action<string>? setStatusText)
    {
        BeginAction(actionKind, isShortcutTrigger: false, setStatusText);
    }

    public static void BeginActionFromShortcut(RaiseHandActionKind actionKind)
    {
        BeginAction(actionKind, isShortcutTrigger: true, setStatusText: null);
    }

    public static string GetActionLabel(RaiseHandActionKind actionKind)
    {
        return actionKind switch
        {
            RaiseHandActionKind.Restart => RestartLabel,
            RaiseHandActionKind.SoftRestart => SoftRestartLabel,
            RaiseHandActionKind.PreviousStep => PreviousStepLabel,
            RaiseHandActionKind.PreviousFloor => PreviousFloorLabel,
            _ => actionKind.ToString()
        };
    }

    public static string GetConfirmPopupTitle(RaiseHandActionKind actionKind)
    {
        return ConfirmTitleText;
    }

    public static string GetConfirmPopupBody(RaiseHandActionKind actionKind)
    {
        return actionKind switch
        {
            RaiseHandActionKind.Restart => BuildCenteredBody(
                "\u4ece\u5934\u518d\u6765\uff01",
                "\u4f60\u60f3\u597d\u4e86\u5417\u52c7\u58eb",
                "\u653e\u5f03\u4e00\u5207\u4ece\u5934\u518d\u6765",
                "\u4f60\u5c06\u5931\u53bb\u73b0\u5728\u7684\u4e00\u5207"),
            RaiseHandActionKind.SoftRestart => BuildCenteredBody(
                "\u8fd9\u6b21\u4e0d\u7b97\uff01",
                "\u56de\u5230\u6218\u6597\u5f00\u59cb\u9636\u6bb5",
                "\u8fd9\u6b21\u4e00\u5b9a\u8981\u627e\u5230",
                "\u6700\u5b8c\u7f8e\u7684\u65f6\u95f4\u7ebf"),
            RaiseHandActionKind.PreviousStep => BuildCenteredBody(
                "\u56de\u5230\u4e0a\u4e00\u6b65\uff01",
                "\u62a5\u544a\u6559\u7ec3",
                "\u6211\u8981\u6094\u68cb",
                "\u6211\u8fd8\u6709\u64cd\u4f5c\u6ca1\u6253\u51fa\u6765"),
            RaiseHandActionKind.PreviousFloor => BuildCenteredBody(
                "\u56de\u5230\u4e0a\u4e00\u5c42!",
                "\u6211\u7684\u670b\u53cb",
                "\u8fd9\u6761\u8def\u597d\u50cf\u4e0d\u5bf9\u52b2",
                "\u56de\u5934\u6362\u4e00\u6761\u8bd5\u8bd5\u770b"),
            _ => BuildCenteredBody(GetActionLabel(actionKind))
        };
    }

    public static bool TryGetActionKind(string actionLabel, out RaiseHandActionKind actionKind)
    {
        if (actionLabel == RestartLabel)
        {
            actionKind = RaiseHandActionKind.Restart;
            return true;
        }

        if (actionLabel == SoftRestartLabel)
        {
            actionKind = RaiseHandActionKind.SoftRestart;
            return true;
        }

        if (actionLabel == PreviousStepLabel)
        {
            actionKind = RaiseHandActionKind.PreviousStep;
            return true;
        }

        if (actionLabel == PreviousFloorLabel)
        {
            actionKind = RaiseHandActionKind.PreviousFloor;
            return true;
        }

        actionKind = default;
        return false;
    }

    private static void BeginAction(
        RaiseHandActionKind actionKind,
        bool isShortcutTrigger,
        Action<string>? setStatusText)
    {
        if (!isShortcutTrigger || RaiseHandHotkeySettingsService.ShouldShowShortcutConfirmPopup())
        {
            ShowConfirmPopup(actionKind, setStatusText);
            return;
        }

        ConfirmAction(actionKind, setStatusText);
    }

    private static void ConfirmAction(RaiseHandActionKind actionKind, Action<string>? setStatusText)
    {
        MainFile.Logger.Info($"RaiseHand action confirmed: {GetActionLabel(actionKind)}");

        if (TryHandleMultiplayerApproval(actionKind, setStatusText))
            return;

        switch (actionKind)
        {
            case RaiseHandActionKind.Restart:
                SetStatusText(setStatusText, "\u5df2\u786e\u8ba4\u4ece\u5934\u518d\u6765\uff0c\u6b63\u5728\u51c6\u5907\u5f00\u542f\u65b0\u4e00\u5c40\u3002");
                TaskHelper.RunSafely(RestartCurrentSingleplayerRun(setStatusText));
                return;
            case RaiseHandActionKind.SoftRestart:
                SetStatusText(setStatusText, "\u5df2\u786e\u8ba4\u8fd9\u6b21\u4e0d\u7b97\uff0c\u6b63\u5728\u8bfb\u53d6\u6700\u8fd1\u7684\u5b58\u6863\u8282\u70b9\u3002");
                TaskHelper.RunSafely(ReloadCurrentSingleplayerRunFromSave(setStatusText));
                return;
            case RaiseHandActionKind.PreviousFloor:
                SetStatusText(setStatusText, "\u5df2\u786e\u8ba4\u56de\u5230\u4e0a\u4e00\u5c42\uff0c\u6b63\u5728\u5bfb\u627e\u4e0a\u4e00\u5c42\u7684\u5feb\u7167\u3002");
                TaskHelper.RunSafely(ReloadPreviousFloorFromBackup(setStatusText));
                return;
            case RaiseHandActionKind.PreviousStep:
                SetStatusText(setStatusText, "\u5df2\u786e\u8ba4\u56de\u5230\u4e0a\u4e00\u6b65\uff0c\u6b63\u5728\u5bfb\u627e\u4e0a\u4e00\u56de\u5408\u7684\u5feb\u7167\u3002");
                TaskHelper.RunSafely(ReloadPreviousCombatStepFromBackup(setStatusText));
                return;
            default:
                SetStatusText(setStatusText, $"\u5df2\u786e\u8ba4 {GetActionLabel(actionKind)}\u3002");
                return;
        }
    }

    private static bool TryHandleMultiplayerApproval(RaiseHandActionKind actionKind, Action<string>? setStatusText)
    {
        if (!MultiplayerApprovalService.IsMultiplayerActive())
            return false;

        var evaluationState = RunManager.Instance.DebugOnlyGetState();
        var localDecision = MultiplayerApprovalPrecheckService.Evaluate(actionKind, evaluationState);
        if (!localDecision.CanProceed)
        {
            SetStatusText(setStatusText, localDecision.StatusText);
            if (localDecision.ShouldShowPopup)
                ShowInfoPopup(localDecision.PopupTitle, localDecision.PopupBody);
            return true;
        }

        if (MultiplayerApprovalService.TryBeginApproval(actionKind, out var statusMessage))
        {
            SetStatusText(setStatusText, statusMessage);
            return true;
        }

        return false;
    }

    private static void ShowConfirmPopup(RaiseHandActionKind actionKind, Action<string>? setStatusText)
    {
        if (NModalContainer.Instance == null)
        {
            MainFile.Logger.Warn("RaiseHand confirm popup skipped because NModalContainer.Instance is null.");
            return;
        }

        if (NModalContainer.Instance.OpenModal != null)
        {
            MainFile.Logger.Warn("RaiseHand confirm popup skipped because another modal is already open.");
            return;
        }

        var popup = NDisconnectConfirmPopup.Create();
        if (popup == null)
        {
            MainFile.Logger.Warn("RaiseHand confirm popup skipped because popup creation returned null.");
            return;
        }

        NModalContainer.Instance.Add(popup, true);
        Callable.From(() => ConfigureConfirmPopup(popup, actionKind, setStatusText)).CallDeferred();
    }

    private static void ConfigureConfirmPopup(
        NDisconnectConfirmPopup popup,
        RaiseHandActionKind actionKind,
        Action<string>? setStatusText)
    {
        var verticalPopup = popup.GetNodeOrNull<NVerticalPopup>("VerticalPopup");
        if (verticalPopup == null)
        {
            MainFile.Logger.Warn("RaiseHand confirm popup skipped because VerticalPopup node was not found.");
            NModalContainer.Instance?.Clear();
            return;
        }

        verticalPopup.DisconnectSignals();
        verticalPopup.DisconnectHotkeys();
        verticalPopup.SetText(GetConfirmPopupTitle(actionKind), GetConfirmPopupBody(actionKind));
        verticalPopup.YesButton.IsYes = true;
        verticalPopup.NoButton.IsYes = false;
        verticalPopup.YesButton.SetText(ConfirmButtonText);
        verticalPopup.NoButton.SetText(CancelButtonText);
        verticalPopup.InitYesButton(new LocString("main_menu_ui", "GENERIC_POPUP.confirm"),
            _ => ConfirmAction(actionKind, setStatusText));
        verticalPopup.InitNoButton(new LocString("main_menu_ui", "GENERIC_POPUP.cancel"), _ => { });
    }

    private static string BuildCenteredBody(params string[] lines)
    {
        return string.Join("\n", lines.Select(line => $"[center]{line}[/center]"));
    }

    private static void ShowInfoPopup(string titleText, string bodyText)
    {
        if (NModalContainer.Instance == null)
        {
            MainFile.Logger.Warn("RaiseHand info popup skipped because NModalContainer.Instance is null.");
            return;
        }

        if (NModalContainer.Instance.OpenModal != null)
        {
            MainFile.Logger.Info("RaiseHand info popup is clearing the current modal before showing the info message.");
            NModalContainer.Instance.Clear();
        }

        Callable.From(() => CreateInfoPopup(titleText, bodyText)).CallDeferred();
    }

    private static void CreateInfoPopup(string titleText, string bodyText)
    {
        if (NModalContainer.Instance == null)
            return;

        var popup = NDisconnectConfirmPopup.Create();
        if (popup == null)
        {
            MainFile.Logger.Warn("RaiseHand info popup skipped because popup creation returned null.");
            return;
        }

        NModalContainer.Instance.Add(popup, true);
        Callable.From(() => ConfigureInfoPopup(popup, titleText, bodyText)).CallDeferred();
    }

    private static void ConfigureInfoPopup(NDisconnectConfirmPopup popup, string titleText, string bodyText)
    {
        var verticalPopup = popup.GetNodeOrNull<NVerticalPopup>("VerticalPopup");
        if (verticalPopup == null)
        {
            MainFile.Logger.Warn("RaiseHand info popup skipped because VerticalPopup node was not found.");
            NModalContainer.Instance?.Clear();
            return;
        }

        verticalPopup.DisconnectSignals();
        verticalPopup.DisconnectHotkeys();
        verticalPopup.SetText(titleText, bodyText);
        verticalPopup.YesButton.IsYes = true;
        verticalPopup.YesButton.SetText(InfoButtonText);
        verticalPopup.NoButton.Visible = false;
        verticalPopup.NoButton.FocusMode = Control.FocusModeEnum.None;
        verticalPopup.InitYesButton(new LocString("main_menu_ui", "GENERIC_POPUP.confirm"), _ => { });
    }

    private static async System.Threading.Tasks.Task RestartCurrentSingleplayerRun(Action<string>? setStatusText)
    {
        try
        {
            if (!RunManager.Instance.IsSinglePlayerOrFakeMultiplayer)
            {
                SetStatusText(setStatusText, "\u5f53\u524d\u7248\u672c\u7684\u91cd\u5f00\u5148\u53ea\u652f\u6301\u5355\u4eba\u5c40\u3002");
                return;
            }

            var runState = RunManager.Instance.DebugOnlyGetState();
            if (runState == null)
            {
                SetStatusText(setStatusText, "\u91cd\u5f00\u5931\u8d25\uff1a\u672a\u627e\u5230\u5f53\u524d run \u72b6\u6001\u3002");
                return;
            }

            var localPlayer = LocalContext.GetMe(runState) ?? runState.Players.FirstOrDefault();
            if (localPlayer == null)
            {
                SetStatusText(setStatusText, "\u91cd\u5f00\u5931\u8d25\uff1a\u672a\u627e\u5230\u672c\u5730\u73a9\u5bb6\u4fe1\u606f\u3002");
                return;
            }

            var character = ModelDb.GetById<CharacterModel>(localPlayer.Character.Id);
            var acts = runState.Acts.Select(act => ModelDb.GetById<ActModel>(act.Id)).ToList();
            var modifiers = runState.Modifiers
                .Select(modifier => ModifierModel.FromSerializable(modifier.ToSerializable()))
                .ToList();
            var seed = RunManager.Instance.DailyTime != null
                ? runState.Rng.StringSeed
                : SeedHelper.GetRandomSeed(10);
            var ascensionLevel = runState.AscensionLevel;
            var dailyTime = RunManager.Instance.DailyTime;
            var gameMode = DeriveCurrentSingleplayerGameMode(runState);

            SetStatusText(setStatusText, "\u6b63\u5728\u91cd\u5f00\u2026");

            NCapstoneContainer.Instance?.Close();
            if (NGame.Instance.CurrentRunNode != null)
            {
                await NGame.Instance.Transition.FadeOut(0.8f, "res://materials/transitions/fade_transition_mat.tres", null);
            }

            RunManager.Instance.CleanUp(true);
            SaveManager.Instance.DeleteCurrentRun();
            await NGame.Instance.StartNewSingleplayerRun(character, true, acts, modifiers, seed, gameMode, ascensionLevel, dailyTime);
        }
        catch (System.Exception e)
        {
            SetStatusText(setStatusText, "\u91cd\u5f00\u5931\u8d25\uff0c\u8bf7\u67e5\u770b\u65e5\u5fd7\u3002");
            MainFile.Logger.Error($"Restart action failed: {e}");
        }
    }

    private static async System.Threading.Tasks.Task ReloadCurrentSingleplayerRunFromSave(Action<string>? setStatusText)
    {
        try
        {
            if (!RunManager.Instance.IsSinglePlayerOrFakeMultiplayer)
            {
                SetStatusText(setStatusText, "\u5f53\u524d\u7248\u672c\u7684\u5c40\u90e8\u91cd\u5f00\u5148\u53ea\u652f\u6301\u5355\u4eba\u5c40\u3002");
                return;
            }

            var runState = RunManager.Instance.DebugOnlyGetState();
            if (IsAtNeowStage(runState))
            {
                SetStatusText(setStatusText, "\u6b63\u5728\u56de\u5230\u521a\u8fdb\u5165\u6e38\u620f\u65f6\u7684\u9636\u6bb5\u2026");
                await ReturnToRunStartStage(setStatusText);
                return;
            }

            if (runState?.CurrentRoom is MapRoom)
            {
                SetStatusText(setStatusText, RaiseHandPopupText.MapOnlyTitleText);
                ShowInfoPopup(RaiseHandPopupText.MapOnlyTitleText, RaiseHandPopupText.MapOnlyBodyText);
                return;
            }

            var readResult = SaveManager.Instance.LoadRunSave();
            if (readResult == null || !readResult.Success || readResult.SaveData == null)
            {
                SetStatusText(setStatusText, "\u5c40\u90e8\u91cd\u5f00\u5931\u8d25\uff1a\u672a\u627e\u5230\u53ef\u7528\u7684 current_run.save\u3002");
                return;
            }

            var serializableRun = readResult.SaveData;
            var restoredRunState = RunState.FromSerializable(serializableRun);

            SetStatusText(setStatusText, "\u6b63\u5728\u5c40\u90e8\u91cd\u5f00\u2026");

            NCapstoneContainer.Instance?.Close();
            if (NGame.Instance.CurrentRunNode != null)
            {
                await NGame.Instance.Transition.FadeOut(0.8f, "res://materials/transitions/fade_transition_mat.tres", null);
            }

            RunManager.Instance.CleanUp(true);
            NGame.Instance.ReactionContainer.InitializeNetworking(new NetSingleplayerGameService());
            RunManager.Instance.SetUpSavedSinglePlayer(restoredRunState, serializableRun);
            await NGame.Instance.LoadRun(restoredRunState, serializableRun.PreFinishedRoom);
            await NGame.Instance.Transition.FadeIn(0.8f, "res://materials/transitions/fade_transition_mat.tres", null);
        }
        catch (System.Exception e)
        {
            SetStatusText(setStatusText, "\u5c40\u90e8\u91cd\u5f00\u5931\u8d25\uff0c\u8bf7\u67e5\u770b\u65e5\u5fd7\u3002");
            MainFile.Logger.Error($"Soft restart action failed: {e}");
        }
    }

    private static async System.Threading.Tasks.Task ReloadPreviousFloorFromBackup(Action<string>? setStatusText)
    {
        try
        {
            if (!RunManager.Instance.IsSinglePlayerOrFakeMultiplayer)
            {
                SetStatusText(setStatusText, "\u5f53\u524d\u7248\u672c\u7684\u56de\u9000\u5230\u4e0a\u4e00\u5c42\u5148\u53ea\u652f\u6301\u5355\u4eba\u5c40\u3002");
                return;
            }

            if (!HasPreviousFloorSnapshotForCurrentMode())
            {
                if (IsAtFirstFloorBoundary())
                {
                    SetStatusText(setStatusText, "\u4f60\u5df2\u65e0\u8def\u53ef\u9000\u3002");
                    ShowInfoPopup(RaiseHandPopupText.NoWayBackTitleText, RaiseHandPopupText.NoWayBackBodyText);
                    return;
                }

                SetStatusText(setStatusText, "\u56de\u5230\u4e0a\u4e00\u5c42\u5931\u8d25\uff1a\u672a\u627e\u5230\u4e0a\u4e00\u5c42\u5feb\u7167\u3002");
                return;
            }

            var snapshotRun = RestoreLatestPreviousFloorSnapshotForCurrentMode();
            SetStatusText(setStatusText, "\u6b63\u5728\u56de\u9000\u5230\u4e0a\u4e00\u5c42\u2026");

            await LoadRunIntoMapSelection(snapshotRun);
        }
        catch (System.Exception e)
        {
            SetStatusText(setStatusText, "\u56de\u9000\u5230\u4e0a\u4e00\u5c42\u5931\u8d25\uff0c\u8bf7\u67e5\u770b\u65e5\u5fd7\u3002");
            MainFile.Logger.Error($"Previous floor action failed: {e}");
        }
    }

    private static async System.Threading.Tasks.Task LoadRunIntoMapSelection(SerializableRun serializableRun)
    {
        var runState = RunState.FromSerializable(serializableRun);

        NCapstoneContainer.Instance?.Close();
        if (NGame.Instance.CurrentRunNode != null)
        {
            await NGame.Instance.Transition.FadeOut(0.8f, "res://materials/transitions/fade_transition_mat.tres", null);
        }

        RunManager.Instance.CleanUp(true);
        NGame.Instance.ReactionContainer.InitializeNetworking(new NetSingleplayerGameService());
        RunManager.Instance.SetUpSavedSinglePlayer(runState, serializableRun);

        await PreloadManager.LoadRunAssets(runState.Players.Select(player => player.Character));
        await PreloadManager.LoadActAssets(runState.Act);

        RunManager.Instance.Launch();
        NGame.Instance.RootSceneContainer.SetCurrentScene(NRun.Create(runState));
        await RunManager.Instance.GenerateMap();
        await RunManager.Instance.EnterRoom(new MapRoom());

        if (RunManager.Instance.MapDrawingsToLoad != null)
        {
            NRun.Instance.GlobalUi.MapScreen.Drawings.LoadDrawings(RunManager.Instance.MapDrawingsToLoad);
            RunManager.Instance.MapDrawingsToLoad = null;
        }

        var mapScreen = NMapScreen.Instance;
        mapScreen?.SetTravelEnabled(true);
        mapScreen?.Open(false);
        mapScreen?.RefreshAllMapPointVotes();

        await NGame.Instance.Transition.FadeIn(0.8f, "res://materials/transitions/fade_transition_mat.tres", null);
    }

    private static async System.Threading.Tasks.Task ReloadPreviousCombatStepFromBackup(Action<string>? setStatusText)
    {
        try
        {
            if (!RunManager.Instance.IsSinglePlayerOrFakeMultiplayer)
            {
                SetStatusText(setStatusText, "\u5f53\u524d\u7248\u672c\u7684\u56de\u9000\u5230\u4e0a\u4e00\u6b65\u5148\u53ea\u652f\u6301\u5355\u4eba\u5c40\u3002");
                return;
            }

            var runState = RunManager.Instance.DebugOnlyGetState();
            var currentRoom = runState?.CurrentRoom;
            var combatRoom = currentRoom as CombatRoom;
            if (combatRoom == null)
            {
                if (currentRoom is MapRoom)
                {
                    SetStatusText(setStatusText, RaiseHandPopupText.MapOnlyTitleText);
                    ShowInfoPopup(RaiseHandPopupText.MapOnlyTitleText, RaiseHandPopupText.MapOnlyBodyText);
                    return;
                }

                SetStatusText(setStatusText, "\u56de\u9000\u5230\u4e0a\u4e00\u6b65\u5f53\u524d\u5148\u53ea\u652f\u6301\u6218\u6597\u4e2d\u4f7f\u7528\u3002");
                return;
            }

            if (combatRoom.IsPreFinished)
            {
                SetStatusText(setStatusText, "\u6218\u6597\u5df2\u7ed3\u675f\u3002");
                ShowInfoPopup(RaiseHandPopupText.CombatFinishedStepTitleText, RaiseHandPopupText.CombatFinishedStepBodyText);
                return;
            }

            var bossEliteRoomKind = SingleplayerBossElitePreviousStepService.ResolveRoomKind(runState, combatRoom);
            if (bossEliteRoomKind != SingleplayerBossElitePreviousStepService.CombatRoomKind.Normal)
            {
                await ReloadBossOrElitePreviousCombatStepFromBackup(runState!, combatRoom, bossEliteRoomKind, setStatusText);
                return;
            }

            var currentRound = combatRoom.CombatState.RoundNumber;
            if (currentRound <= 1)
            {
                SetStatusText(setStatusText, "\u4f60\u5df2\u65e0\u8def\u53ef\u9000\u3002");
                ShowInfoPopup(RaiseHandPopupText.NoWayBackStepTitleText, RaiseHandPopupText.NoWayBackStepBodyText);
                return;
            }

            if (currentRound == 2)
            {
                SetStatusText(setStatusText, "\u6b63\u5728\u56de\u9000\u5230\u8fdb\u5165\u623f\u95f4\u65f6\u7684\u72b6\u6001\u2026");
                await ReloadCurrentSingleplayerRunFromSave(setStatusText);
                return;
            }

            var targetRound = currentRound - 1;
            if (!HasPreviousStepSnapshotForCurrentMode(targetRound))
            {
                SetStatusText(setStatusText, "\u56de\u9000\u5230\u4e0a\u4e00\u6b65\u5931\u8d25\uff1a\u672a\u627e\u5230\u4e0a\u4e00\u56de\u5408\u5feb\u7167\u3002");
                return;
            }

            var snapshot = RestorePreviousStepSnapshotForCurrentMode(targetRound);
            SetStatusText(setStatusText, "\u6b63\u5728\u56de\u9000\u5230\u4e0a\u4e00\u6b65\u2026");
            await LoadRunIntoCurrentRoom(snapshot.RunSnapshot, snapshot.RoomSnapshot, snapshot.CombatStateJson);
        }
        catch (System.Exception e)
        {
            SetStatusText(setStatusText, "\u56de\u9000\u5230\u4e0a\u4e00\u6b65\u5931\u8d25\uff0c\u8bf7\u67e5\u770b\u65e5\u5fd7\u3002");
            MainFile.Logger.Error($"Previous step action failed: {e}");
        }
    }

    private static async System.Threading.Tasks.Task ReloadBossOrElitePreviousCombatStepFromBackup(
        RunState runState,
        CombatRoom combatRoom,
        SingleplayerBossElitePreviousStepService.CombatRoomKind roomKind,
        Action<string>? setStatusText)
    {
        var currentRound = combatRoom.CombatState.RoundNumber;
        if (currentRound <= 1)
        {
            SetStatusText(setStatusText, "\u4f60\u5df2\u65e0\u8def\u53ef\u9000\u3002");
            ShowInfoPopup(RaiseHandPopupText.NoWayBackStepTitleText, RaiseHandPopupText.NoWayBackStepBodyText);
            return;
        }

        if (currentRound == 2)
        {
            SingleplayerBossElitePreviousStepService.LogRoomStartRestoreRequested(roomKind, combatRoom, currentRound);
            SetStatusText(setStatusText, SingleplayerBossElitePreviousStepService.GetRoomStartStatusText(roomKind));
            await ReloadCurrentSingleplayerRunFromSave(setStatusText);
            return;
        }

        var targetRound = currentRound - 1;
        if (!HasPreviousStepSnapshotForCurrentMode(targetRound))
        {
            SetStatusText(setStatusText, "\u56de\u9000\u5230\u4e0a\u4e00\u6b65\u5931\u8d25\uff1a\u672a\u627e\u5230\u4e0a\u4e00\u56de\u5408\u5feb\u7167\u3002");
            return;
        }

        SingleplayerBossElitePreviousStepService.LogRestoreRequested(roomKind, combatRoom, currentRound, targetRound);
        var snapshot = RestorePreviousStepSnapshotForCurrentMode(targetRound);
        var restoreContext = SingleplayerBossElitePreviousStepService.GetRestoreContext(roomKind);

        SetStatusText(setStatusText, SingleplayerBossElitePreviousStepService.GetRestoreStatusText(roomKind));
        await LoadRunIntoCurrentRoom(snapshot.RunSnapshot, snapshot.RoomSnapshot, snapshot.CombatStateJson, restoreContext);
        SingleplayerBossElitePreviousStepService.LogRestoreCompleted(roomKind, RunManager.Instance.DebugOnlyGetState());
    }

    private static async System.Threading.Tasks.Task LoadRunIntoCurrentRoom(
        SerializableRun serializableRun,
        SerializableRoom roomSnapshot,
        string? combatStateJson)
    {
        await LoadRunIntoCurrentRoom(serializableRun, roomSnapshot, combatStateJson, "singleplayer previous step");
    }

    private static async System.Threading.Tasks.Task LoadRunIntoCurrentRoom(
        SerializableRun serializableRun,
        SerializableRoom roomSnapshot,
        string? combatStateJson,
        string restoreContext)
    {
        var runState = RunState.FromSerializable(serializableRun);

        NCapstoneContainer.Instance?.Close();
        if (NGame.Instance.CurrentRunNode != null)
        {
            await NGame.Instance.Transition.FadeOut(0.8f, "res://materials/transitions/fade_transition_mat.tres", null);
        }

        RunManager.Instance.CleanUp(true);
        NGame.Instance.ReactionContainer.InitializeNetworking(new NetSingleplayerGameService());
        RunManager.Instance.SetUpSavedSinglePlayer(runState, serializableRun);
        await PreloadManager.LoadRunAssets(runState.Players.Select(player => player.Character));
        await PreloadManager.LoadActAssets(runState.Act);

        RunManager.Instance.Launch();
        NGame.Instance.RootSceneContainer.SetCurrentScene(NRun.Create(runState));
        await RunManager.Instance.GenerateMap();

        var restoredRoom = CreateRoomFromPreviousStepSnapshot(roomSnapshot, runState);

        try
        {
            if (restoredRoom is CombatRoom)
                SingleplayerPreviousStepRestoreStateService.BeginRestore();

            await RunManager.Instance.EnterRoom(restoredRoom);
            await SingleplayerCombatStateSnapshotService.TryApplyCombatStateJsonAsync(combatStateJson, runState, restoreContext);
        }
        finally
        {
            SingleplayerPreviousStepRestoreStateService.EndRestore();
            await NGame.Instance.Transition.FadeIn(0.8f, "res://materials/transitions/fade_transition_mat.tres", null);
        }
    }

    private static AbstractRoom CreateRoomFromPreviousStepSnapshot(SerializableRoom roomSnapshot, RunState runState)
    {
        return roomSnapshot.RoomType switch
        {
            RoomType.Shop => new MerchantRoom(),
            RoomType.RestSite => new RestSiteRoom(),
            _ => AbstractRoom.FromSerializable(roomSnapshot, runState)
        };
    }

    private static async System.Threading.Tasks.Task ReturnToRunStartStage(Action<string>? setStatusText)
    {
        var runState = RunManager.Instance.DebugOnlyGetState();
        if (runState == null)
        {
            SetStatusText(setStatusText, "\u56de\u5230\u5f00\u5c40\u5931\u8d25\uff1a\u672a\u627e\u5230\u5f53\u524d run \u72b6\u6001\u3002");
            return;
        }

        var localPlayer = LocalContext.GetMe(runState) ?? runState.Players.FirstOrDefault();
        if (localPlayer == null)
        {
            SetStatusText(setStatusText, "\u56de\u5230\u5f00\u5c40\u5931\u8d25\uff1a\u672a\u627e\u5230\u672c\u5730\u73a9\u5bb6\u4fe1\u606f\u3002");
            return;
        }

        var character = ModelDb.GetById<CharacterModel>(localPlayer.Character.Id);
        var acts = runState.Acts.Select(act => ModelDb.GetById<ActModel>(act.Id)).ToList();
        var modifiers = runState.Modifiers
            .Select(modifier => ModifierModel.FromSerializable(modifier.ToSerializable()))
            .ToList();
        var seed = runState.Rng.StringSeed;
        var ascensionLevel = runState.AscensionLevel;
        var dailyTime = RunManager.Instance.DailyTime;
        var gameMode = DeriveCurrentSingleplayerGameMode(runState);

        NCapstoneContainer.Instance?.Close();
        if (NGame.Instance.CurrentRunNode != null)
        {
            await NGame.Instance.Transition.FadeOut(0.8f, "res://materials/transitions/fade_transition_mat.tres", null);
        }

        RunManager.Instance.CleanUp(true);
        SaveManager.Instance.DeleteCurrentRun();
        await NGame.Instance.StartNewSingleplayerRun(character, true, acts, modifiers, seed, gameMode, ascensionLevel, dailyTime);
    }

    private static bool IsAtFirstFloorBoundary()
    {
        var runState = RunManager.Instance.DebugOnlyGetState();
        return runState != null && runState.TotalFloor <= 1;
    }

    private static bool IsAtNeowStage(RunState? runState)
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

    private static GameMode DeriveCurrentSingleplayerGameMode(RunState runState)
    {
        if (RunManager.Instance.DailyTime != null)
            return GameMode.Daily;

        return runState.Modifiers.Count > 0 ? GameMode.Custom : GameMode.Standard;
    }

    private static bool HasPreviousFloorSnapshotForCurrentMode()
    {
        return NonStandardSingleplayerRunIdentity.ShouldUseIsolatedSnapshots(RunManager.Instance)
            ? NonStandardPreviousFloorSnapshotService.HasSnapshot(RunManager.Instance)
            : PreviousFloorSnapshotService.HasSnapshot(RunManager.Instance);
    }

    private static SerializableRun RestoreLatestPreviousFloorSnapshotForCurrentMode()
    {
        return NonStandardSingleplayerRunIdentity.ShouldUseIsolatedSnapshots(RunManager.Instance)
            ? NonStandardPreviousFloorSnapshotService.RestoreLatestSnapshot(RunManager.Instance)
            : PreviousFloorSnapshotService.RestoreLatestSnapshot(RunManager.Instance);
    }

    private static bool HasPreviousStepSnapshotForCurrentMode(int roundNumber)
    {
        return SingleplayerPreviousStepSnapshotCoordinator.HasSnapshotForCurrentMode(roundNumber);
    }

    private static PreviousStepSnapshotService.RestoredStepSnapshot RestorePreviousStepSnapshotForCurrentMode(int roundNumber)
    {
        return SingleplayerPreviousStepSnapshotCoordinator.RestoreSnapshotForCurrentMode(roundNumber);
    }

    private static void SetStatusText(Action<string>? setStatusText, string text)
    {
        setStatusText?.Invoke(text);
    }
}
