using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using HandsUp.HandsUpCode.Multiplayer;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Multiplayer.Game.PeerInput;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Audio;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.Unlocks;

namespace HandsUp.HandsUpCode.Services;

public static class MultiplayerExecutionService
{
    private static readonly PropertyInfo? StateProperty = typeof(RunManager)
        .GetProperty("State", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly MethodInfo? InitializeSharedMethod = typeof(RunManager)
        .GetMethod("InitializeShared", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly MethodInfo? InitializeNewRunMethod = typeof(RunManager)
        .GetMethod("InitializeNewRun", BindingFlags.Instance | BindingFlags.NonPublic);

    public static async Task ExecuteApprovedActionAsync(RaiseHandActionKind actionKind, string? runJson, string? roomJson, string? sourceRoomType, string? combatStateJson = null, string? restoreHint = null)
    {
        switch (actionKind)
        {
            case RaiseHandActionKind.Restart:
                await RestartCurrentMultiplayerRun(runJson);
                break;
            case RaiseHandActionKind.SoftRestart:
                await ReloadCurrentMultiplayerRun(runJson, sourceRoomType, combatStateJson, restoreHint);
                break;
            case RaiseHandActionKind.PreviousFloor:
                await ReloadPreviousFloorInMultiplayer(runJson);
                break;
            case RaiseHandActionKind.PreviousStep:
                MainFile.Logger.Warn("Multiplayer previous-step execution is intentionally disabled. The action now only shows a popup and does not perform rollback.");
                break;
            default:
                MainFile.Logger.Info($"Multiplayer execution for {actionKind} is not connected yet.");
                break;
        }
    }

    private static async Task RestartCurrentMultiplayerRun(string? runJson)
    {
        var serializableRun = ParseSerializableRun(runJson, "restart");
        if (serializableRun == null)
            return;

        var sourceRunState = RunState.FromSerializable(serializableRun);
        var netService = RunManager.Instance.NetService;
        if (netService == null || (netService.Type != NetGameType.Host && netService.Type != NetGameType.Client))
        {
            MainFile.Logger.Warn("Multiplayer restart skipped because the current net service is not multiplayer.");
            return;
        }

        var players = sourceRunState.Players
            .Select(player => Player.CreateForNewRun(
                ModelDb.GetById<CharacterModel>(player.Character.Id),
                UnlockState.FromSerializable(player.UnlockState.ToSerializable()),
                player.NetId))
            .ToList();
        var acts = sourceRunState.Acts
            .Select(act => ModelDb.GetById<ActModel>(act.Id).ToMutable())
            .ToList();
        var modifiers = sourceRunState.Modifiers
            .Select(modifier => ModifierModel.FromSerializable(modifier.ToSerializable()))
            .ToList();
        var restartedRun = RunState.CreateForNewRun(
            players,
            acts,
            modifiers,
            DeriveGameMode(serializableRun),
            sourceRunState.AscensionLevel,
            sourceRunState.Rng.StringSeed);

        try
        {
            MultiplayerPreviousFloorSnapshotService.ClearSnapshots("multiplayer restart resetting multiplayer previous-floor history");
            MultiplayerSoftRestartSnapshotService.ClearSnapshot("multiplayer restart resetting multiplayer soft-restart snapshot");

            if (NGame.Instance.CurrentRunNode != null)
            {
                await NGame.Instance.Transition.FadeOut(0.8f, "res://materials/transitions/fade_transition_mat.tres", null);
            }

            DetachCurrentRunSceneBeforeReload();
            using (new NetLoadingHandle(netService))
            {
                CleanUpRunForMultiplayerReload();
                InitializeFreshMultiplayerRun(restartedRun, netService, serializableRun.DailyTime);
                await PreloadManager.LoadRunAssets(restartedRun.Players.Select(player => player.Character));
                await PreloadManager.LoadActAssets(restartedRun.Acts[0]);
                await RunManager.Instance.FinalizeStartingRelics();
                RunManager.Instance.Launch();
                NGame.Instance.RootSceneContainer.SetCurrentScene(NRun.Create(restartedRun));
                await RunManager.Instance.EnterAct(0, false);
            }

            await NGame.Instance.Transition.FadeIn(0.8f, "res://materials/transitions/fade_transition_mat.tres", null);
        }
        catch
        {
            throw;
        }
    }

    private static async Task ReloadCurrentMultiplayerRun(string? runJson, string? sourceRoomType, string? combatStateJson, string? restoreHint)
    {
        var serializableRun = ParseSerializableRun(runJson, "soft restart");
        if (serializableRun == null)
            return;

        MultiplayerSoftRestartSnapshotService.SuppressNextSnapshotForCurrentRoom(RunManager.Instance);
        if (!string.IsNullOrWhiteSpace(combatStateJson))
            MultiplayerInitialCombatPileSnapshotService.ClearPendingInitialDrawOverride("preparing multiplayer combat soft restart");
        await ReloadMultiplayerRunWithDefaultLoad(serializableRun, sourceRoomType, combatStateJson, restoreHint);
    }

    private static async Task ReloadPreviousFloorInMultiplayer(string? runJson)
    {
        var serializableRun = ParseSerializableRun(runJson, "previous floor");
        if (serializableRun == null)
            return;

        MultiplayerPreviousFloorSnapshotService.DiscardLatestSnapshot("multiplayer previous-floor action consumed latest snapshot");
        await LoadMultiplayerRunIntoMapSelection(serializableRun);
    }

    private static async Task ReloadMultiplayerRunWithDefaultLoad(SerializableRun serializableRun, string? sourceRoomType, string? combatStateJson, string? restoreHint)
    {
        var runState = RunState.FromSerializable(serializableRun);
        var loadLobby = CreateLoadLobby(serializableRun);
        if (loadLobby == null)
            return;

        try
        {
            var localSoftRestartSnapshot = MultiplayerSoftRestartSnapshotService.TryReadSnapshot();
            var preFinishedRoomType = serializableRun.PreFinishedRoom?.RoomType.ToString() ?? string.Empty;
            var inferredMapPointType = TryGetSavedCurrentMapPointType(serializableRun, out var mapPointType)
                ? mapPointType.ToString()
                : string.Empty;
            MainFile.Logger.Info($"Beginning multiplayer soft restart reload. NetType={RunManager.Instance.NetService?.Type} RoomType={runState.CurrentRoom?.RoomType} RestoreHint={restoreHint ?? string.Empty}");
            MainFile.Logger.Info($"Multiplayer soft restart payload details: SourceRoomType={sourceRoomType ?? string.Empty} PreFinishedRoomType={preFinishedRoomType} SavedMapPointType={inferredMapPointType}");
            if (NGame.Instance.CurrentRunNode != null)
            {
                await NGame.Instance.Transition.FadeOut(0.8f, "res://materials/transitions/fade_transition_mat.tres", null);
            }

            DetachCurrentRunSceneBeforeReload();
            CleanUpRunForMultiplayerReload();
            RunManager.Instance.SetUpSavedMultiPlayer(runState, loadLobby);

            if (!string.IsNullOrWhiteSpace(combatStateJson))
                MultiplayerInitialCombatPileSnapshotService.ArmSnapshotJsonForNextInitialDraw(combatStateJson, "multiplayer soft restart");

            if (TryCreateUnsupportedNonCombatRoomForLoad(serializableRun, sourceRoomType, runState, out var unsupportedRoom))
            {
                MainFile.Logger.Info($"Using multiplayer custom room restore path for unsupported non-combat room {unsupportedRoom.RoomType}.");
                await LoadMultiplayerRunIntoSpecificRoom(runState, unsupportedRoom, sourceRoomType, localSoftRestartSnapshot);
            }
            else
            {
                var preFinishedRoomForLoad = GetPreFinishedRoomForLoad(serializableRun, sourceRoomType);
                await NGame.Instance.LoadRun(runState, preFinishedRoomForLoad);
            }

            MainFile.Logger.Info($"Completed multiplayer soft restart reload into room {runState.CurrentRoom?.RoomType}.");
            await NGame.Instance.Transition.FadeIn(0.8f, "res://materials/transitions/fade_transition_mat.tres", null);
        }
        finally
        {
            loadLobby.CleanUp(false);
        }
    }

    private static SerializableRoom? GetPreFinishedRoomForLoad(SerializableRun serializableRun, string? sourceRoomType)
    {
        var preFinishedRoom = serializableRun.PreFinishedRoom;
        if (preFinishedRoom == null)
            return null;

        if (IsSupportedPreFinishedRoom(preFinishedRoom.RoomType))
            return preFinishedRoom;

        var inferredMapPointType = TryGetSavedCurrentMapPointType(serializableRun, out var mapPointType)
            ? mapPointType.ToString()
            : string.Empty;
        MainFile.Logger.Info(
            $"Dropping unsupported non-combat PreFinishedRoom {preFinishedRoom.RoomType} for multiplayer soft restart. " +
            $"SourceRoomType={sourceRoomType ?? string.Empty} SavedMapPointType={inferredMapPointType}");
        return null;
    }

    private static bool TryCreateUnsupportedNonCombatRoomForLoad(
        SerializableRun serializableRun,
        string? sourceRoomType,
        RunState runState,
        out AbstractRoom room)
    {
        room = null!;

        var roomType = serializableRun.PreFinishedRoom?.RoomType;
        if (roomType == null
            && System.Enum.TryParse<RoomType>(sourceRoomType, true, out var parsedRoomType))
        {
            roomType = parsedRoomType;
        }

        if (roomType == null || IsSupportedPreFinishedRoom(roomType.Value))
            return false;

        room = roomType.Value switch
        {
            RoomType.Shop => new MerchantRoom(),
            RoomType.RestSite => new RestSiteRoom(),
            RoomType.Treasure => new TreasureRoom(runState.CurrentActIndex),
            _ => null!
        };

        return room != null;
    }

    private static bool IsSupportedPreFinishedRoom(RoomType roomType)
    {
        return roomType is RoomType.Monster or RoomType.Elite or RoomType.Boss or RoomType.Event;
    }

    private static bool TryGetSavedCurrentMapPointType(SerializableRun serializableRun, out MapPointType mapPointType)
    {
        mapPointType = MapPointType.Unassigned;

        if (serializableRun.CurrentActIndex < 0 || serializableRun.CurrentActIndex >= serializableRun.Acts.Count)
            return false;

        var savedMap = serializableRun.Acts[serializableRun.CurrentActIndex].SavedMap;
        if (serializableRun.VisitedMapCoords.Count == 0)
            return false;

        var currentCoord = serializableRun.VisitedMapCoords.LastOrDefault();
        if (savedMap?.Points == null)
            return false;

        var savedPoint = savedMap.Points.FirstOrDefault(point => point.Coord == currentCoord);
        if (savedPoint == null)
            return false;

        mapPointType = savedPoint.PointType;
        return true;
    }

    private static async Task LoadMultiplayerRunIntoSpecificRoom(
        RunState runState,
        AbstractRoom room,
        string? sourceRoomType,
        MultiplayerSoftRestartSnapshotService.MultiplayerSoftRestartSnapshot? localSoftRestartSnapshot)
    {
        await PreloadManager.LoadRunAssets(runState.Players.Select(player => player.Character));
        await PreloadManager.LoadActAssets(runState.Act);

        RunManager.Instance.Launch();
        NGame.Instance.RootSceneContainer.SetCurrentScene(NRun.Create(runState));
        await RunManager.Instance.GenerateMap();
        await RunManager.Instance.LoadIntoLatestMapCoord(room);
        TryApplyTreasureRewardSnapshot(runState, room, sourceRoomType, localSoftRestartSnapshot);

        if (RunManager.Instance.MapDrawingsToLoad != null)
        {
            NRun.Instance.GlobalUi.MapScreen.Drawings.LoadDrawings(RunManager.Instance.MapDrawingsToLoad);
            RunManager.Instance.MapDrawingsToLoad = null;
        }
    }

    private static void TryApplyTreasureRewardSnapshot(
        RunState runState,
        AbstractRoom room,
        string? sourceRoomType,
        MultiplayerSoftRestartSnapshotService.MultiplayerSoftRestartSnapshot? localSoftRestartSnapshot)
    {
        if (room is not TreasureRoom)
            return;

        var roomIdentity = !string.IsNullOrWhiteSpace(sourceRoomType)
            ? sourceRoomType
            : room.RoomType.ToString();
        var expectedRoomScope = $"{runState.TotalFloor:D4}_{roomIdentity}";
        MultiplayerTreasureRewardSnapshotService.ApplySnapshotIfNeeded(
            RunManager.Instance,
            localSoftRestartSnapshot,
            expectedRoomScope,
            "multiplayer treasure soft restart");
    }

    private static async Task LoadMultiplayerRunIntoMapSelection(SerializableRun serializableRun)
    {
        var runState = RunState.FromSerializable(serializableRun);
        var loadLobby = CreateLoadLobby(serializableRun);
        if (loadLobby == null)
            return;

        try
        {
            if (NGame.Instance.CurrentRunNode != null)
            {
                await NGame.Instance.Transition.FadeOut(0.8f, "res://materials/transitions/fade_transition_mat.tres", null);
            }

            DetachCurrentRunSceneBeforeReload();
            CleanUpRunForMultiplayerReload();
            RunManager.Instance.SetUpSavedMultiPlayer(runState, loadLobby);
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
        finally
        {
            loadLobby.CleanUp(false);
        }
    }

    private static LoadRunLobby? CreateLoadLobby(SerializableRun serializableRun)
    {
        var netService = RunManager.Instance.NetService;
        if (netService == null || (netService.Type != NetGameType.Host && netService.Type != NetGameType.Client))
        {
            MainFile.Logger.Warn("Multiplayer reload skipped because the current net service is not multiplayer.");
            return null;
        }

        return new LoadRunLobby(netService, NoOpLoadRunLobbyListener.Instance, serializableRun);
    }

    private static void InitializeFreshMultiplayerRun(RunState runState, INetGameService netService, System.DateTimeOffset? dailyTime)
    {
        var runManager = RunManager.Instance;
        if (StateProperty == null || InitializeSharedMethod == null || InitializeNewRunMethod == null)
            throw new System.InvalidOperationException("Failed to resolve RunManager initialization methods for multiplayer restart.");

        StateProperty.SetValue(runManager, runState);
        InitializeSharedMethod.Invoke(runManager, [netService, new PeerInputSynchronizer(netService), true, dailyTime, System.DateTimeOffset.UtcNow.ToUnixTimeSeconds(), 0L, 0L]);
        runManager.InitializeRunLobby(netService, runState);
        InitializeNewRunMethod.Invoke(runManager, []);
        runManager.GenerateRooms();
    }

    private static SerializableRun? ParseSerializableRun(string? runJson, string actionLabel)
    {
        if (string.IsNullOrWhiteSpace(runJson))
        {
            MainFile.Logger.Warn($"Multiplayer {actionLabel} skipped because no serialized run payload was provided.");
            return null;
        }

        var readResult = SaveManager.FromJson<SerializableRun>(runJson);
        if (!readResult.Success || readResult.SaveData == null)
        {
            MainFile.Logger.Warn($"Multiplayer {actionLabel} skipped because the serialized run payload could not be parsed.");
            return null;
        }

        return readResult.SaveData;
    }

    private static GameMode DeriveGameMode(SerializableRun serializableRun)
    {
        if (serializableRun.DailyTime != null)
            return GameMode.Daily;

        return serializableRun.Modifiers.Count > 0 ? GameMode.Custom : GameMode.Standard;
    }

    private static void DetachCurrentRunSceneBeforeReload()
    {
        var rootSceneContainer = NGame.Instance.RootSceneContainer;
        if (rootSceneContainer.CurrentScene == null)
            return;

        var placeholder = new Control
        {
            Name = "HandsUpReloadPlaceholder",
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        placeholder.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        rootSceneContainer.SetCurrentScene(placeholder);
        MainFile.Logger.Info("Detached current run scene before multiplayer reload so old multiplayer UI can exit tree cleanly.");
    }

    private static void CleanUpRunForMultiplayerReload()
    {
        var runManager = RunManager.Instance;

        runManager.ActionQueueSet.Reset();
        runManager.CombatStateSynchronizer?.Dispose();
        runManager.InputSynchronizer?.Dispose();
        CombatManager.Instance.StateTracker.SetState(null!);
        NAudioManager.Instance?.StopAllLoops();
        NOverlayStack.Instance?.Clear();
        NCapstoneContainer.Instance?.CleanUp();
        NMapScreen.Instance?.CleanUp();
        NModalContainer.Instance?.Clear();
        CombatManager.Instance.Reset(true);
        runManager.CombatReplayWriter.Dispose();
        runManager.ActionQueueSynchronizer.Dispose();
        runManager.PlayerChoiceSynchronizer.Dispose();
        runManager.EventSynchronizer.Dispose();
        runManager.RewardSynchronizer.Dispose();
        runManager.RestSiteSynchronizer.Dispose();
        runManager.OneOffSynchronizer.Dispose();
        runManager.FlavorSynchronizer.Dispose();
        runManager.ChecksumTracker.Dispose();
        runManager.RunLobby?.Dispose();
        LocalContext.NetId = null;
        StateProperty?.SetValue(runManager, null);
    }

    private sealed class NoOpLoadRunLobbyListener : ILoadRunLobbyListener
    {
        public static NoOpLoadRunLobbyListener Instance { get; } = new();

        public void PlayerConnected(ulong playerId)
        {
        }

        public void RemotePlayerDisconnected(ulong playerId)
        {
        }

        public Task<bool> ShouldAllowRunToBegin()
        {
            return Task.FromResult(true);
        }

        public void BeginRun()
        {
        }

        public void PlayerReadyChanged(ulong playerId)
        {
        }

        public void LocalPlayerDisconnected(NetErrorInfo info)
        {
        }
    }

}
