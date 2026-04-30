using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Managers;

namespace HandsUp.HandsUpCode.Services;

public static class MultiplayerSoftRestartSnapshotService
{
    private const string SnapshotFileName = "handsup_multiplayer_soft_restart_snapshot.json";
    private const string ShopPreEntrySnapshotFileName = "handsup_multiplayer_shop_pre_entry_snapshot.json";
    private static string? _suppressedNextSnapshotScope;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    private static string GetSnapshotPath(string fileName)
    {
        return UserDataPathProvider.GetProfileScopedPath(
            SaveManager.Instance.CurrentProfileId,
            Path.Combine(UserDataPathProvider.SavesDir, fileName));
    }

    private static string GetStandardSnapshotPath()
    {
        return GetSnapshotPath(SnapshotFileName);
    }

    private static string GetShopPreEntrySnapshotPath()
    {
        return GetSnapshotPath(ShopPreEntrySnapshotFileName);
    }

    public static void CaptureSnapshotFromCurrentState(RunManager? runManager, string sourceTag)
    {
        var runState = runManager?.DebugOnlyGetState();
        var currentRoom = runState?.CurrentRoom;
        var netType = runManager?.NetService?.Type;
        if (runState == null
            || currentRoom == null
            || currentRoom is MapRoom
            || (netType != NetGameType.Host && netType != NetGameType.Client))
        {
            return;
        }

        var roomScope = BuildRoomScope(runState, currentRoom);
        if (_suppressedNextSnapshotScope == roomScope)
        {
            MainFile.Logger.Info($"Skipped multiplayer soft-restart snapshot capture for {roomScope} after restore.");
            _suppressedNextSnapshotScope = null;
            return;
        }

        var snapshotDirectory = Path.GetDirectoryName(ProjectSettings.GlobalizePath(GetStandardSnapshotPath()));
        if (!string.IsNullOrWhiteSpace(snapshotDirectory))
            Directory.CreateDirectory(snapshotDirectory);

        var payload = new MultiplayerSoftRestartSnapshot
        {
            RunJson = MegaCrit.Sts2.Core.Saves.SaveManager.ToJson(runManager!.ToSave(currentRoom)),
            RoomScope = roomScope,
            SourceRoomType = currentRoom.RoomType.ToString(),
            CombatStateJson = string.Empty,
            TreasureRelicIds = currentRoom is TreasureRoom
                ? runManager.TreasureRoomRelicSynchronizer.CurrentRelics?
                    .Select(relic => relic.Id.ToString())
                    .ToList()
                : null
        };

        var snapshotJson = JsonSerializer.Serialize(payload, JsonOptions);
        var snapshotPath = ProjectSettings.GlobalizePath(GetStandardSnapshotPath());
        File.WriteAllText(snapshotPath, snapshotJson, Encoding.UTF8);

        MainFile.Logger.Info($"Multiplayer soft-restart snapshot written from {sourceTag} to {snapshotPath}");
    }

    public static void CaptureShopPreEntrySnapshotFromCurrentState(RunManager? runManager, string sourceTag)
    {
        var runState = runManager?.DebugOnlyGetState();
        var netType = runManager?.NetService?.Type;
        if (runState == null
            || (netType != NetGameType.Host && netType != NetGameType.Client))
        {
            return;
        }

        var snapshotDirectory = Path.GetDirectoryName(ProjectSettings.GlobalizePath(GetShopPreEntrySnapshotPath()));
        if (!string.IsNullOrWhiteSpace(snapshotDirectory))
            Directory.CreateDirectory(snapshotDirectory);

        var payload = new MultiplayerSoftRestartSnapshot
        {
            RunJson = MegaCrit.Sts2.Core.Saves.SaveManager.ToJson(runManager!.ToSave(null)),
            RoomScope = BuildRoomScope(runState.TotalFloor + 1, RoomType.Shop.ToString()),
            SourceRoomType = RoomType.Shop.ToString(),
            CombatStateJson = string.Empty
        };

        var snapshotJson = JsonSerializer.Serialize(payload, JsonOptions);
        var snapshotPath = ProjectSettings.GlobalizePath(GetShopPreEntrySnapshotPath());
        File.WriteAllText(snapshotPath, snapshotJson, Encoding.UTF8);

        MainFile.Logger.Info($"Multiplayer shop pre-entry soft-restart snapshot written from {sourceTag} to {snapshotPath}");
    }

    public static void CaptureCombatSnapshotFromCurrentState(RunManager? runManager, string sourceTag)
    {
        // Multiplayer soft restart now follows the original "save and reload room entry state" semantics,
        // so combat-ready snapshots are intentionally disabled.
    }

    public static void SuppressNextSnapshotForCurrentRoom(RunManager? runManager)
    {
        var runState = runManager?.DebugOnlyGetState();
        var currentRoom = runState?.CurrentRoom;
        SuppressNextSnapshotForRoom(runState, currentRoom);
    }

    public static void SuppressNextSnapshotForRoom(RunState? runState, AbstractRoom? room)
    {
        if (runState == null || room == null)
            return;

        _suppressedNextSnapshotScope = BuildRoomScope(runState, room);
        MainFile.Logger.Info($"Armed multiplayer soft-restart snapshot suppression for {_suppressedNextSnapshotScope}.");
    }

    public static void ClearSnapshot(string reason)
    {
        ClearSnapshotFile(GetStandardSnapshotPath(), $"Cleared multiplayer soft-restart snapshot: {reason}");
        ClearSnapshotFile(GetShopPreEntrySnapshotPath(), $"Cleared multiplayer shop pre-entry soft-restart snapshot: {reason}");
        _suppressedNextSnapshotScope = null;
    }

    private static string BuildRoomScope(RunState runState, AbstractRoom currentRoom)
    {
        var roomIdentity = currentRoom.ModelId?.ToString() ?? currentRoom.RoomType.ToString();
        return BuildRoomScope(runState.TotalFloor, roomIdentity);
    }

    private static string BuildRoomScope(int totalFloor, string roomIdentity)
    {
        return $"{totalFloor:D4}_{roomIdentity}";
    }

    public static MultiplayerSoftRestartSnapshot? TryReadSnapshot()
    {
        return TryReadSnapshotFile(GetStandardSnapshotPath(), "multiplayer soft-restart snapshot");
    }

    public static MultiplayerSoftRestartSnapshot? TryReadShopPreEntrySnapshotForCurrentShop(RunState? runState)
    {
        if (runState?.CurrentRoom?.RoomType != RoomType.Shop)
            return null;

        var snapshot = TryReadSnapshotFile(GetShopPreEntrySnapshotPath(), "multiplayer shop pre-entry soft-restart snapshot");
        if (snapshot == null)
            return null;

        var expectedRoomScope = BuildRoomScope(runState.TotalFloor, RoomType.Shop.ToString());
        if (snapshot.RoomScope != expectedRoomScope)
        {
            MainFile.Logger.Info(
                $"Ignoring multiplayer shop pre-entry soft-restart snapshot because room scope {snapshot.RoomScope} does not match current shop scope {expectedRoomScope}.");
            return null;
        }

        return snapshot;
    }

    private static MultiplayerSoftRestartSnapshot? TryReadSnapshotFile(string snapshotPath, string snapshotLabel)
    {
        var globalizedSnapshotPath = ProjectSettings.GlobalizePath(snapshotPath);
        if (!File.Exists(globalizedSnapshotPath))
            return null;

        try
        {
            return JsonSerializer.Deserialize<MultiplayerSoftRestartSnapshot>(File.ReadAllText(globalizedSnapshotPath, Encoding.UTF8), JsonOptions);
        }
        catch (JsonException e)
        {
            MainFile.Logger.Warn($"Failed to parse {snapshotLabel} {globalizedSnapshotPath}: {e.Message}");
            return null;
        }
    }

    private static void ClearSnapshotFile(string snapshotPath, string logMessage)
    {
        var globalizedSnapshotPath = ProjectSettings.GlobalizePath(snapshotPath);
        if (!File.Exists(globalizedSnapshotPath))
            return;

        File.Delete(globalizedSnapshotPath);
        MainFile.Logger.Info(logMessage);
    }

    public sealed class MultiplayerSoftRestartSnapshot
    {
        public string RunJson { get; set; } = string.Empty;
        public string RoomScope { get; set; } = string.Empty;
        public string SourceRoomType { get; set; } = string.Empty;
        public string CombatStateJson { get; set; } = string.Empty;
        public List<string>? TreasureRelicIds { get; set; }
    }
}
