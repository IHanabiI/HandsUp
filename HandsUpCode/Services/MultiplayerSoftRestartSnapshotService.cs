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
    private static string? _suppressedNextSnapshotScope;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    private static string GetSnapshotPath()
    {
        return UserDataPathProvider.GetProfileScopedPath(
            SaveManager.Instance.CurrentProfileId,
            Path.Combine(UserDataPathProvider.SavesDir, SnapshotFileName));
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

        var snapshotDirectory = Path.GetDirectoryName(ProjectSettings.GlobalizePath(GetSnapshotPath()));
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
        var snapshotPath = ProjectSettings.GlobalizePath(GetSnapshotPath());
        File.WriteAllText(snapshotPath, snapshotJson, Encoding.UTF8);

        MainFile.Logger.Info($"Multiplayer soft-restart snapshot written from {sourceTag} to {snapshotPath}");
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
        if (runState == null || currentRoom == null)
            return;

        _suppressedNextSnapshotScope = BuildRoomScope(runState, currentRoom);
    }

    public static void ClearSnapshot(string reason)
    {
        var snapshotPath = ProjectSettings.GlobalizePath(GetSnapshotPath());
        if (!File.Exists(snapshotPath))
            return;

        File.Delete(snapshotPath);
        _suppressedNextSnapshotScope = null;
        MainFile.Logger.Info($"Cleared multiplayer soft-restart snapshot: {reason}");
    }

    private static string BuildRoomScope(RunState runState, AbstractRoom currentRoom)
    {
        var roomIdentity = currentRoom.ModelId?.ToString() ?? currentRoom.RoomType.ToString();
        return $"{runState.TotalFloor:D4}_{roomIdentity}";
    }

    public static MultiplayerSoftRestartSnapshot? TryReadSnapshot()
    {
        var snapshotPath = ProjectSettings.GlobalizePath(GetSnapshotPath());
        if (!File.Exists(snapshotPath))
            return null;

        try
        {
            return JsonSerializer.Deserialize<MultiplayerSoftRestartSnapshot>(File.ReadAllText(snapshotPath, Encoding.UTF8), JsonOptions);
        }
        catch (JsonException e)
        {
            MainFile.Logger.Warn($"Failed to parse multiplayer soft-restart snapshot {snapshotPath}: {e.Message}");
            return null;
        }
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
