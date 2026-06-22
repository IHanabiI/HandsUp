using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using HandsUp.HandsUpCode.Multiplayer;
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
using MegaCrit.Sts2.Core.Nodes.Screens.PauseMenu;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.MapDrawing;
using MegaCrit.Sts2.Core.Saves.Runs;
using HandsUp.HandsUpCode.Services;

namespace HandsUp.HandsUpCode.UI;

public partial class RaiseHandSubmenu : NSubmenu
{
    private static readonly RaiseHandActionKind[] ActionKinds = RaiseHandActionService.OrderedActionKinds.ToArray();

    private const string RestartLabel = "\u4ece\u5934\u518d\u6765";
    private const string SoftRestartLabel = "\u8fd9\u6b21\u4e0d\u7b97";
    private const string PreviousStepLabel = "\u56de\u5230\u4e0a\u4e00\u6b65";
    private const string PreviousFloorLabel = "\u56de\u5230\u4e0a\u4e00\u5c42";
    private const string TitleText = "\u4e3e\u624b\u624b";
    private const string DescriptionText = "\u4f60\u662f\u5426\u89c9\u5f97\u81ea\u5df1\u8fd8\u6709\u64cd\u4f5c\u6ca1\u6709\u6253\u51fa\u6765\uff0c\u8fd8\u662f\u5fc3\u5b58\u4e0d\u7518\uff1f\u6765\u5427\uff0c\u63a5\u53d7\u795e\u79d8\u529b\u91cf\u7684\u5e2e\u52a9\uff0c\u9006\u8f6c\u65f6\u7a7a\u3002";
    private const string PlaceholderStatus = "\u5f53\u524d\u9636\u6bb5\u5148\u9a8c\u8bc1\u5165\u53e3\u4e0e\u754c\u9762\uff0c\u56db\u4e2a\u529f\u80fd\u6309\u94ae\u6682\u4e3a\u5360\u4f4d\u3002";
    private const string ConfirmTitleText = "\u6ce8\u5165\u795e\u79d8\u529b\u91cf";
    private const string ConfirmButtonText = "\u786e\u5b9a";
    private const string CancelButtonText = "\u53d6\u6d88";
    private const string InfoButtonText = "\u786e\u5b9a";

    private readonly VBoxContainer _root = new();
    private readonly VBoxContainer _buttonContainer = new();
    private readonly Label _description = new();
    private readonly Label _status = new();

    private readonly List<NPauseMenuButton> _actionButtons = [];
    private NPauseMenuButton? _templateButton;

    protected override Control? InitialFocusedControl => _actionButtons.Count > 0 ? _actionButtons[0] : null;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;

        var backButton = PreloadManager.Cache.GetScene(SceneHelper.GetScenePath("ui/back_button"))
            .Instantiate<NBackButton>();
        backButton.Name = "BackButton";
        AddChild(backButton);

        _root.Name = "RaiseHandRoot";
        _root.SetAnchorsPreset(LayoutPreset.Center);
        _root.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        _root.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        _root.Position = new Vector2(420f, 180f);
        _root.CustomMinimumSize = new Vector2(1080f, 620f);
        _root.AddThemeConstantOverride("separation", 18);
        AddChild(_root);

        var title = new Label
        {
            Text = TitleText,
            HorizontalAlignment = HorizontalAlignment.Center,
            CustomMinimumSize = new Vector2(0f, 56f)
        };
        title.AddThemeFontOverride("font", PreloadManager.Cache.GetAsset<Font>("res://themes/kreon_regular_glyph_space_one.tres"));
        title.AddThemeFontSizeOverride("font_size", 42);
        title.AddThemeColorOverride("font_color", StsColors.gold);
        _root.AddChild(title);

        _description.Text = DescriptionText;
        _description.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _description.HorizontalAlignment = HorizontalAlignment.Center;
        _description.CustomMinimumSize = new Vector2(960f, 80f);
        _description.AddThemeColorOverride("font_color", StsColors.cream);
        _description.AddThemeFontSizeOverride("font_size", 22);
        _root.AddChild(_description);

        _buttonContainer.Name = "ActionButtons";
        _buttonContainer.AddThemeConstantOverride("separation", 14);
        _buttonContainer.CustomMinimumSize = new Vector2(720f, 360f);
        _root.AddChild(_buttonContainer);

        _status.Text = PlaceholderStatus;
        _status.HorizontalAlignment = HorizontalAlignment.Center;
        _status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _status.AddThemeColorOverride("font_color", StsColors.gray);
        _status.AddThemeFontSizeOverride("font_size", 18);
        _status.CustomMinimumSize = new Vector2(920f, 60f);
        _root.AddChild(_status);

        ConnectSignals();
    }

    public void InitializeFromPauseMenu(NPauseMenu pauseMenu)
    {
        _templateButton ??= pauseMenu.GetNodeOrNull<Control>("%ButtonContainer")
            ?.GetNodeOrNull<NPauseMenuButton>("Settings");

        if (_templateButton == null)
        {
            MainFile.Logger.Warn("RaiseHandSubmenu could not find a pause menu button template.");
            return;
        }

        if (_actionButtons.Count == 0)
            BuildButtons();
    }

    private void BuildButtons()
    {
        if (_templateButton == null)
            return;

        var duplicateFlags =
            (int)(Node.DuplicateFlags.Groups | Node.DuplicateFlags.Scripts | Node.DuplicateFlags.UseInstantiation);

        foreach (var actionKind in ActionKinds)
        {
            var label = RaiseHandActionService.GetActionLabel(actionKind);
            var button = (NPauseMenuButton)_templateButton.Duplicate(duplicateFlags);
            button.Name = $"{actionKind}Button";
            button.GetNodeOrNull<Label>("Label")?.Set("text", label);
            button.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(_ => OnActionPressed(actionKind)));
            _buttonContainer.AddChild(button);
            _actionButtons.Add(button);
        }

        for (var i = 0; i < _actionButtons.Count; i++)
        {
            var current = _actionButtons[i];
            current.FocusNeighborLeft = current.GetPath();
            current.FocusNeighborRight = current.GetPath();
            current.FocusNeighborTop = (i > 0 ? _actionButtons[i - 1] : current).GetPath();
            current.FocusNeighborBottom = (i < _actionButtons.Count - 1 ? _actionButtons[i + 1] : current).GetPath();
        }
    }

    private void SetStatusText(string text)
    {
        if (!GodotObject.IsInstanceValid(this) || !GodotObject.IsInstanceValid(_status))
            return;

        _status.Text = text;
    }

    private void OnActionPressed(RaiseHandActionKind actionKind)
    {
        RaiseHandActionService.BeginActionFromMenu(actionKind, SetStatusText);
    }

    private void ConfirmAction(string actionLabel)
    {
        MainFile.Logger.Info($"RaiseHand submenu action confirmed: {actionLabel}");

        if (TryHandleMultiplayerApproval(actionLabel))
            return;

        if (actionLabel == RestartLabel)
        {
            SetStatusText("\u5df2\u786e\u8ba4\u4ece\u5934\u518d\u6765\uff0c\u6b63\u5728\u51c6\u5907\u5f00\u542f\u65b0\u4e00\u5c40\u3002");
            TaskHelper.RunSafely(RestartCurrentSingleplayerRun());
            return;
        }

        if (actionLabel == SoftRestartLabel)
        {
            SetStatusText("\u5df2\u786e\u8ba4\u8fd9\u6b21\u4e0d\u7b97\uff0c\u6b63\u5728\u8bfb\u53d6\u6700\u8fd1\u7684\u5b58\u6863\u8282\u70b9\u3002");
            TaskHelper.RunSafely(ReloadCurrentSingleplayerRunFromSave());
            return;
        }

        if (actionLabel == PreviousFloorLabel)
        {
            SetStatusText("\u5df2\u786e\u8ba4\u56de\u5230\u4e0a\u4e00\u5c42\uff0c\u6b63\u5728\u5bfb\u627e\u4e0a\u4e00\u5c42\u7684\u5feb\u7167\u3002");
            TaskHelper.RunSafely(ReloadPreviousFloorFromBackup());
            return;
        }

        if (actionLabel == PreviousStepLabel)
        {
            SetStatusText("\u5df2\u786e\u8ba4\u56de\u5230\u4e0a\u4e00\u6b65\uff0c\u6b63\u5728\u5bfb\u627e\u4e0a\u4e00\u56de\u5408\u7684\u5feb\u7167\u3002");
            TaskHelper.RunSafely(ReloadPreviousCombatStepFromBackup());
            return;
        }

        SetStatusText($"\u5df2\u786e\u8ba4 {actionLabel}\u3002\u5f53\u524d\u9636\u6bb5\u5148\u628a\u786e\u8ba4\u6d41\u7a0b\u8dd1\u901a\uff0c\u771f\u5b9e\u56de\u9000\u903b\u8f91\u4e0b\u4e00\u6b65\u63a5\u5165\u3002");
    }

    private bool TryHandleMultiplayerApproval(string actionLabel)
    {
        if (!MultiplayerApprovalService.IsMultiplayerActive())
            return false;

        if (!TryMapActionKind(actionLabel, out var actionKind))
            return false;

        if (MultiplayerApprovalService.TryBeginApproval(actionKind, out var statusMessage))
        {
            SetStatusText(statusMessage);
            return true;
        }

        return false;
    }

    private static bool TryMapActionKind(string actionLabel, out RaiseHandActionKind actionKind)
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

    private void ShowConfirmPopup(string actionLabel)
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
        Callable.From(() => ConfigureConfirmPopup(popup, actionLabel)).CallDeferred();
    }

    private void ConfigureConfirmPopup(NDisconnectConfirmPopup popup, string actionLabel)
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
        if (TryMapActionKind(actionLabel, out var actionKind))
        {
            verticalPopup.SetText(
                RaiseHandActionService.GetConfirmPopupTitle(actionKind),
                RaiseHandActionService.GetConfirmPopupBody(actionKind));
        }
        else
        {
            verticalPopup.SetText(ConfirmTitleText, $"[center]{actionLabel}[/center]");
        }
        verticalPopup.YesButton.IsYes = true;
        verticalPopup.NoButton.IsYes = false;
        verticalPopup.YesButton.SetText(ConfirmButtonText);
        verticalPopup.NoButton.SetText(CancelButtonText);
        verticalPopup.InitYesButton(new LocString("main_menu_ui", "GENERIC_POPUP.confirm"), _ => ConfirmAction(actionLabel));
        verticalPopup.InitNoButton(new LocString("main_menu_ui", "GENERIC_POPUP.cancel"), _ => { });
    }

    private async System.Threading.Tasks.Task RestartCurrentSingleplayerRun()
    {
        try
        {
            if (!RunManager.Instance.IsSingleplayerOrFakeMultiplayer)
            {
                SetStatusText("\u5f53\u524d\u7248\u672c\u7684\u91cd\u5f00\u5148\u53ea\u652f\u6301\u5355\u4eba\u5c40\u3002");
                return;
            }

            var runState = RunManager.Instance.DebugOnlyGetState();
            if (runState == null)
            {
                SetStatusText("\u91cd\u5f00\u5931\u8d25\uff1a\u672a\u627e\u5230\u5f53\u524d run \u72b6\u6001\u3002");
                return;
            }

            var localPlayer = LocalContext.GetMe(runState) ?? runState.Players.FirstOrDefault();
            if (localPlayer == null)
            {
                SetStatusText("\u91cd\u5f00\u5931\u8d25\uff1a\u672a\u627e\u5230\u672c\u5730\u73a9\u5bb6\u4fe1\u606f\u3002");
                return;
            }

            var character = ModelDb.GetById<CharacterModel>(localPlayer.Character.Id);
            var acts = runState.Acts.Select(act => ModelDb.GetById<ActModel>(act.Id)).ToList();
            var modifiers = runState.Modifiers
                .Select(modifier => ModifierModel.FromSerializable(modifier.ToSerializable()))
                .ToList();
            var seed = RunManager.Instance.DailyTime != null
                ? runState.Rng.StringSeed
                : SeedHelper.GetRandomSeed(runState.Rng.Niche, 10);
            var ascensionLevel = runState.AscensionLevel;
            var dailyTime = RunManager.Instance.DailyTime;
            var gameMode = DeriveCurrentSingleplayerGameMode(runState);

            SetStatusText("\u6b63\u5728\u91cd\u5f00\u2026");

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
            SetStatusText("\u91cd\u5f00\u5931\u8d25\uff0c\u8bf7\u67e5\u770b\u65e5\u5fd7\u3002");
            MainFile.Logger.Error($"Restart action failed: {e}");
        }
    }

    private async System.Threading.Tasks.Task ReloadCurrentSingleplayerRunFromSave()
    {
        try
        {
            if (!RunManager.Instance.IsSingleplayerOrFakeMultiplayer)
            {
                SetStatusText("\u5f53\u524d\u7248\u672c\u7684\u5c40\u90e8\u91cd\u5f00\u5148\u53ea\u652f\u6301\u5355\u4eba\u5c40\u3002");
                return;
            }

            var runState = RunManager.Instance.DebugOnlyGetState();
            if (IsAtNeowStage(runState))
            {
                SetStatusText("\u6b63\u5728\u56de\u5230\u521a\u8fdb\u5165\u6e38\u620f\u65f6\u7684\u9636\u6bb5\u2026");
                await ReturnToRunStartStage();
                return;
            }

            if (runState?.CurrentRoom is MapRoom)
            {
                SetStatusText(RaiseHandPopupText.MapOnlyTitleText);
                ShowInfoPopup(RaiseHandPopupText.MapOnlyTitleText, RaiseHandPopupText.MapOnlyBodyText);
                return;
            }

            var readResult = SaveManager.Instance.LoadRunSave();
            if (readResult == null || !readResult.Success || readResult.SaveData == null)
            {
                SetStatusText("\u5c40\u90e8\u91cd\u5f00\u5931\u8d25\uff1a\u672a\u627e\u5230\u53ef\u7528\u7684 current_run.save\u3002");
                return;
            }

            var serializableRun = readResult.SaveData;
            var restoredRunState = RunState.FromSerializable(serializableRun);

            SetStatusText("\u6b63\u5728\u5c40\u90e8\u91cd\u5f00\u2026");

            NCapstoneContainer.Instance?.Close();
            if (NGame.Instance.CurrentRunNode != null)
            {
                await NGame.Instance.Transition.FadeOut(0.8f, "res://materials/transitions/fade_transition_mat.tres", null);
            }

            RunManager.Instance.CleanUp(true);
            NGame.Instance.ReactionContainer.InitializeNetworking(new NetSingleplayerGameService());
            RunManager.Instance.SetUpSavedSingleplayer(restoredRunState, serializableRun);
            await NGame.Instance.LoadRun(restoredRunState, serializableRun.PreFinishedRoom);
            await NGame.Instance.Transition.FadeIn(0.8f, "res://materials/transitions/fade_transition_mat.tres", null);
        }
        catch (System.Exception e)
        {
            SetStatusText("\u5c40\u90e8\u91cd\u5f00\u5931\u8d25\uff0c\u8bf7\u67e5\u770b\u65e5\u5fd7\u3002");
            MainFile.Logger.Error($"Soft restart action failed: {e}");
        }
    }

    private async System.Threading.Tasks.Task ReloadPreviousFloorFromBackup()
    {
        try
        {
            if (!RunManager.Instance.IsSingleplayerOrFakeMultiplayer)
            {
                SetStatusText("\u5f53\u524d\u7248\u672c\u7684\u56de\u9000\u5230\u4e0a\u4e00\u5c42\u5148\u53ea\u652f\u6301\u5355\u4eba\u5c40\u3002");
                return;
            }

            if (!HasPreviousFloorSnapshotForCurrentMode())
            {
                if (IsAtFirstFloorBoundary())
                {
                    SetStatusText("\u4f60\u5df2\u65e0\u8def\u53ef\u9000\u3002");
                    ShowInfoPopup(RaiseHandPopupText.NoWayBackTitleText, RaiseHandPopupText.NoWayBackBodyText);
                    return;
                }

                SetStatusText("\u56de\u9000\u5230\u4e0a\u4e00\u5c42\u5931\u8d25\uff1a\u672a\u627e\u5230上一层快照。");
                return;
            }

            var snapshotRun = RestoreLatestPreviousFloorSnapshotForCurrentMode();
            SetStatusText("\u6b63\u5728\u56de\u9000\u5230\u4e0a\u4e00\u5c42\u2026");

            await LoadRunIntoMapSelection(snapshotRun);
        }
        catch (System.Exception e)
        {
            SetStatusText("\u56de\u9000\u5230\u4e0a\u4e00\u5c42\u5931\u8d25\uff0c\u8bf7\u67e5\u770b\u65e5\u5fd7\u3002");
            MainFile.Logger.Error($"Previous floor action failed: {e}");
        }
    }

    private async System.Threading.Tasks.Task LoadRunIntoMapSelection(SerializableRun serializableRun)
    {
        var runState = RunState.FromSerializable(serializableRun);
        var mapDrawingsToRestore = serializableRun.MapDrawings;

        NCapstoneContainer.Instance?.Close();
        if (NGame.Instance.CurrentRunNode != null)
        {
            await NGame.Instance.Transition.FadeOut(0.8f, "res://materials/transitions/fade_transition_mat.tres", null);
        }

        RunManager.Instance.CleanUp(true);
        NGame.Instance.ReactionContainer.InitializeNetworking(new NetSingleplayerGameService());
        RunManager.Instance.SetUpSavedSingleplayer(runState, serializableRun);

        await PreloadManager.LoadRunAssets(runState.Players.Select(player => player.Character));
        await PreloadManager.LoadActAssets(runState.Act);

        RunManager.Instance.Launch();
        NGame.Instance.RootSceneContainer.SetCurrentScene(NRun.Create(runState));
        await RunManager.Instance.GenerateMap();
        await RunManager.Instance.EnterRoom(new MapRoom());

        if (RunManager.Instance.MapDrawingsToLoad != null)
        {
            NRun.Instance.GlobalUi.MapScreen.Drawings.LoadDrawings(RunManager.Instance.MapDrawingsToLoad);
            mapDrawingsToRestore = RunManager.Instance.MapDrawingsToLoad;
            RunManager.Instance.MapDrawingsToLoad = null;
        }

        var mapScreen = NMapScreen.Instance;
        mapScreen?.SetTravelEnabled(true);
        mapScreen?.Open(false);
        mapScreen?.RefreshAllMapPointVotes();
        await RestoreMapDrawingsAfterMapSelectionReloadAsync(mapDrawingsToRestore);

        await NGame.Instance.Transition.FadeIn(0.8f, "res://materials/transitions/fade_transition_mat.tres", null);
    }

    private async System.Threading.Tasks.Task RestoreMapDrawingsAfterMapSelectionReloadAsync(SerializableMapDrawings? mapDrawings)
    {
        if (mapDrawings == null)
            return;

        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        var drawings = NMapScreen.Instance?.Drawings;
        if (drawings == null)
        {
            MainFile.Logger.Warn("Skipped delayed map drawing restore because NMapScreen.Drawings was unavailable.");
            return;
        }

        drawings.ClearAllLines();
        drawings.LoadDrawings(mapDrawings);

        MainFile.Logger.Info(
            $"Reloaded map drawings after previous-floor restore. PlayerDrawingSets={mapDrawings.drawings.Count}.");
    }

    private async System.Threading.Tasks.Task ReloadPreviousCombatStepFromBackup()
    {
        try
        {
            if (!RunManager.Instance.IsSingleplayerOrFakeMultiplayer)
            {
                SetStatusText("\u5f53\u524d\u7248\u672c\u7684\u56de\u9000\u5230\u4e0a\u4e00\u6b65\u5148\u53ea\u652f\u6301\u5355\u4eba\u5c40\u3002");
                return;
            }

            var runState = RunManager.Instance.DebugOnlyGetState();
            var currentRoom = runState?.CurrentRoom;
            var combatRoom = currentRoom as CombatRoom;
            if (combatRoom == null)
            {
                if (currentRoom is MapRoom)
                {
                    SetStatusText(RaiseHandPopupText.MapOnlyTitleText);
                    ShowInfoPopup(RaiseHandPopupText.MapOnlyTitleText, RaiseHandPopupText.MapOnlyBodyText);
                    return;
                }

                SetStatusText("\u56de\u9000\u5230\u4e0a\u4e00\u6b65\u5f53\u524d\u5148\u53ea\u652f\u6301\u6218\u6597\u4e2d\u4f7f\u7528\u3002");
                return;
            }

            if (combatRoom.IsPreFinished)
            {
                SetStatusText("\u6218\u6597\u5df2\u7ed3\u675f\u3002");
                ShowInfoPopup(RaiseHandPopupText.CombatFinishedStepTitleText, RaiseHandPopupText.CombatFinishedStepBodyText);
                return;
            }

            var bossEliteRoomKind = SingleplayerBossElitePreviousStepService.ResolveRoomKind(runState, combatRoom);
            if (bossEliteRoomKind != SingleplayerBossElitePreviousStepService.CombatRoomKind.Normal)
            {
                await ReloadBossOrElitePreviousCombatStepFromBackup(runState!, combatRoom, bossEliteRoomKind);
                return;
            }

            var currentRound = combatRoom.CombatState.RoundNumber;
            if (currentRound <= 1)
            {
                SetStatusText("\u4f60\u5df2\u65e0\u8def\u53ef\u9000\u3002");
                ShowInfoPopup(RaiseHandPopupText.NoWayBackStepTitleText, RaiseHandPopupText.NoWayBackStepBodyText);
                return;
            }

            if (currentRound == 2)
            {
                if (SingleplayerEventCombatPreviousStepService.ShouldTreatAsStandaloneCombat(combatRoom))
                {
                    const int combatStartRound = 1;
                    if (!HasPreviousStepSnapshotForCurrentMode(combatStartRound))
                    {
                        SetStatusText("\u56de\u9000\u5230\u4e0a\u4e00\u6b65\u5931\u8d25\uff1a\u672a\u627e\u5230\u6218\u6597\u5f00\u59cb\u5feb\u7167\u3002");
                        return;
                    }

                    var combatStartSnapshot = RestorePreviousStepSnapshotForCurrentMode(combatStartRound);
                    SetStatusText("\u6b63\u5728\u56de\u9000\u5230\u6218\u6597\u5f00\u59cb\u65f6\u7684\u72b6\u6001\u2026");
                    await LoadRunIntoCurrentRoom(
                        combatStartSnapshot.RunSnapshot,
                        combatStartSnapshot.RoomSnapshot,
                        combatStartSnapshot.CombatStateJson,
                        "singleplayer event combat previous step");
                    return;
                }

                SetStatusText("\u6b63\u5728\u56de\u9000\u5230\u8fdb\u5165\u623f\u95f4\u65f6\u7684\u72b6\u6001\u2026");
                await ReloadCurrentSingleplayerRunFromSave();
                return;
            }

            var targetRound = currentRound - 1;
            if (!HasPreviousStepSnapshotForCurrentMode(targetRound))
            {
                SetStatusText("\u56de\u9000\u5230\u4e0a\u4e00\u6b65\u5931\u8d25\uff1a\u672a\u627e\u5230\u4e0a\u4e00\u56de\u5408\u5feb\u7167\u3002");
                return;
            }

            var snapshot = RestorePreviousStepSnapshotForCurrentMode(targetRound);
            SetStatusText("\u6b63\u5728\u56de\u9000\u5230\u4e0a\u4e00\u6b65\u2026");
            await LoadRunIntoCurrentRoom(snapshot.RunSnapshot, snapshot.RoomSnapshot, snapshot.CombatStateJson);
        }
        catch (System.Exception e)
        {
            SetStatusText("\u56de\u9000\u5230\u4e0a\u4e00\u6b65\u5931\u8d25\uff0c\u8bf7\u67e5\u770b\u65e5\u5fd7\u3002");
            MainFile.Logger.Error($"Previous step action failed: {e}");
        }
    }

    private async System.Threading.Tasks.Task ReloadBossOrElitePreviousCombatStepFromBackup(
        RunState runState,
        CombatRoom combatRoom,
        SingleplayerBossElitePreviousStepService.CombatRoomKind roomKind)
    {
        var currentRound = combatRoom.CombatState.RoundNumber;
        if (currentRound <= 1)
        {
            SetStatusText("\u4f60\u5df2\u65e0\u8def\u53ef\u9000\u3002");
            ShowInfoPopup(RaiseHandPopupText.NoWayBackStepTitleText, RaiseHandPopupText.NoWayBackStepBodyText);
            return;
        }

        if (currentRound == 2)
        {
            SingleplayerBossElitePreviousStepService.LogRoomStartRestoreRequested(roomKind, combatRoom, currentRound);
            SetStatusText(SingleplayerBossElitePreviousStepService.GetRoomStartStatusText(roomKind));
            await ReloadCurrentSingleplayerRunFromSave();
            return;
        }

        var targetRound = currentRound - 1;
        if (!HasPreviousStepSnapshotForCurrentMode(targetRound))
        {
            SetStatusText("\u56de\u9000\u5230\u4e0a\u4e00\u6b65\u5931\u8d25\uff1a\u672a\u627e\u5230\u4e0a\u4e00\u56de\u5408\u5feb\u7167\u3002");
            return;
        }

        SingleplayerBossElitePreviousStepService.LogRestoreRequested(roomKind, combatRoom, currentRound, targetRound);
        var snapshot = RestorePreviousStepSnapshotForCurrentMode(targetRound);
        var restoreContext = SingleplayerBossElitePreviousStepService.GetRestoreContext(roomKind);

        SetStatusText(SingleplayerBossElitePreviousStepService.GetRestoreStatusText(roomKind));
        await LoadRunIntoCurrentRoom(snapshot.RunSnapshot, snapshot.RoomSnapshot, snapshot.CombatStateJson, restoreContext);
        SingleplayerBossElitePreviousStepService.LogRestoreCompleted(roomKind, RunManager.Instance.DebugOnlyGetState());
    }

    private async System.Threading.Tasks.Task LoadRunIntoCurrentRoom(SerializableRun serializableRun, SerializableRoom roomSnapshot, string? combatStateJson)
    {
        await LoadRunIntoCurrentRoom(serializableRun, roomSnapshot, combatStateJson, "singleplayer previous step");
    }

    private async System.Threading.Tasks.Task LoadRunIntoCurrentRoom(
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
        RunManager.Instance.SetUpSavedSingleplayer(runState, serializableRun);
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

    private async System.Threading.Tasks.Task ReturnToRunStartStage()
    {
        var runState = RunManager.Instance.DebugOnlyGetState();
        if (runState == null)
        {
            SetStatusText("\u56de\u5230\u5f00\u5c40\u5931\u8d25\uff1a\u672a\u627e\u5230\u5f53\u524d run \u72b6\u6001\u3002");
            return;
        }

        var localPlayer = LocalContext.GetMe(runState) ?? runState.Players.FirstOrDefault();
        if (localPlayer == null)
        {
            SetStatusText("\u56de\u5230\u5f00\u5c40\u5931\u8d25\uff1a\u672a\u627e\u5230\u672c\u5730\u73a9\u5bb6\u4fe1\u606f\u3002");
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

    private bool IsAtFirstFloorBoundary()
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

    private void ShowInfoPopup(string titleText, string bodyText)
    {
        if (NModalContainer.Instance == null)
        {
            MainFile.Logger.Warn("RaiseHand info popup skipped because NModalContainer.Instance is null.");
            return;
        }

        if (NModalContainer.Instance.OpenModal != null)
        {
            MainFile.Logger.Info("RaiseHand info popup is clearing the current modal before showing the no-way-back message.");
            NModalContainer.Instance.Clear();
        }

        Callable.From(() => CreateInfoPopup(titleText, bodyText)).CallDeferred();
    }

    private void CreateInfoPopup(string titleText, string bodyText)
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

    private void ConfigureInfoPopup(NDisconnectConfirmPopup popup, string titleText, string bodyText)
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
        verticalPopup.NoButton.FocusMode = FocusModeEnum.None;
        verticalPopup.InitYesButton(new LocString("main_menu_ui", "GENERIC_POPUP.confirm"), _ => { });
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

}
