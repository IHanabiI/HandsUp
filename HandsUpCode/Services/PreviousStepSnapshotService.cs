using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Managers;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace HandsUp.HandsUpCode.Services;

public static class PreviousStepSnapshotService
{
    private const string SnapshotDirectoryName = "handsup_previous_step_history";
    private const string SnapshotFileExtension = ".json";
    private static string? _suppressedNextCaptureKey;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    private static string GetSnapshotDirectoryPath()
    {
        return UserDataPathProvider.GetProfileScopedPath(
            SaveManager.Instance.CurrentProfileId,
            Path.Combine(UserDataPathProvider.SavesDir, SnapshotDirectoryName));
    }

    public static void CaptureSnapshotFromCurrentCombatStep(RunManager? runManager, string sourceTag)
    {
        var payload = BuildSnapshotPayload(runManager);
        if (payload == null)
            return;

        ClearSnapshotsIfScopeChanged(payload.RoomScopeKey);
        WriteSnapshotPayload(payload, sourceTag);
    }

    public static void ClearSnapshots(string reason)
    {
        var snapshotDirectory = ProjectSettings.GlobalizePath(GetSnapshotDirectoryPath());
        if (!Directory.Exists(snapshotDirectory))
            return;

        foreach (var snapshotFile in Directory.GetFiles(snapshotDirectory, $"step_*{SnapshotFileExtension}", SearchOption.TopDirectoryOnly))
        {
            File.Delete(snapshotFile);
        }

        _suppressedNextCaptureKey = null;
        MainFile.Logger.Info($"Cleared previous-step snapshots: {reason}");
    }

    private static void WriteSnapshotPayload(StepSnapshotPayload payload, string sourceTag)
    {
        if (_suppressedNextCaptureKey == payload.StepKey)
        {
            MainFile.Logger.Info($"Skipped previous-step snapshot capture for {payload.StepKey} after restore.");
            _suppressedNextCaptureKey = null;
            return;
        }

        var existingSnapshotForSameStep = GetSnapshotFiles()
            .Where(snapshotFile => ReadStepKey(snapshotFile.FullName) == payload.StepKey)
            .ToList();

        if (existingSnapshotForSameStep.Count > 0)
        {
            foreach (var snapshotFile in existingSnapshotForSameStep)
            {
                File.Delete(snapshotFile.FullName);
            }

            MainFile.Logger.Info($"Overwriting previous-step snapshot node for {payload.StepKey}.");
        }

        var snapshotDirectory = ProjectSettings.GlobalizePath(GetSnapshotDirectoryPath());
        Directory.CreateDirectory(snapshotDirectory);

        var snapshotPath = Path.Combine(snapshotDirectory, GetSnapshotFileName(payload));
        var snapshotJson = JsonSerializer.Serialize(payload, JsonOptions);
        File.WriteAllText(snapshotPath, snapshotJson, Encoding.UTF8);

        MainFile.Logger.Info($"Previous-step snapshot written from {sourceTag} to {snapshotPath}");
    }

    public static bool HasSnapshot()
    {
        return GetSnapshotFiles().Count > 0;
    }

    public static bool HasSnapshotForRound(int roundNumber)
    {
        return TryGetSnapshotFileForRound(roundNumber) != null;
    }

    public static RestoredStepSnapshot RestoreLatestSnapshot()
    {
        var latestSnapshot = GetLatestSnapshotFile()
                             ?? throw new FileNotFoundException("Previous step snapshot was not found.");
        var snapshot = ReadSnapshot(latestSnapshot.FullName);

        _suppressedNextCaptureKey = snapshot.StepKey;
        MainFile.Logger.Info($"Previous-step snapshot restored from {latestSnapshot.FullName} without deleting the snapshot node.");
        return snapshot;
    }

    public static RestoredStepSnapshot PeekLatestSnapshot()
    {
        var latestSnapshot = GetLatestSnapshotFile()
                             ?? throw new FileNotFoundException("Previous step snapshot was not found.");
        return ReadSnapshot(latestSnapshot.FullName);
    }

    public static RestoredStepSnapshot RestoreSnapshotForRound(int roundNumber)
    {
        var snapshotFile = TryGetSnapshotFileForRound(roundNumber)
                           ?? throw new FileNotFoundException($"Previous step snapshot for round {roundNumber} was not found.");
        var snapshot = ReadSnapshot(snapshotFile.FullName);

        _suppressedNextCaptureKey = snapshot.StepKey;
        MainFile.Logger.Info($"Previous-step snapshot for round {roundNumber} restored from {snapshotFile.FullName} without deleting the snapshot node.");
        return snapshot;
    }

    public static RestoredStepSnapshot PeekSnapshotForRound(int roundNumber)
    {
        var snapshotFile = TryGetSnapshotFileForRound(roundNumber)
                           ?? throw new FileNotFoundException($"Previous step snapshot for round {roundNumber} was not found.");
        return ReadSnapshot(snapshotFile.FullName);
    }

    public static void DiscardLatestSnapshot(string reason)
    {
        var latestSnapshot = GetLatestSnapshotFile();
        if (latestSnapshot == null)
            return;

        var snapshot = ReadSnapshot(latestSnapshot.Value.FullName);
        _suppressedNextCaptureKey = snapshot.StepKey;
        File.Delete(latestSnapshot.Value.FullName);

        MainFile.Logger.Info($"Discarded previous-step snapshot {latestSnapshot.Value.FullName}: {reason}");
    }

    public static void DiscardSnapshotForRound(int roundNumber, string reason)
    {
        var snapshotFile = TryGetSnapshotFileForRound(roundNumber);
        if (snapshotFile == null)
            return;

        var snapshot = ReadSnapshot(snapshotFile.Value.FullName);
        _suppressedNextCaptureKey = snapshot.StepKey;
        File.Delete(snapshotFile.Value.FullName);

        MainFile.Logger.Info($"Discarded previous-step snapshot for round {roundNumber} {snapshotFile.Value.FullName}: {reason}");
    }

    private static StepSnapshotPayload? BuildSnapshotPayload(RunManager? runManager)
    {
        var runState = runManager?.DebugOnlyGetState();
        if (runState == null)
            return null;

        if (!runManager!.IsSinglePlayerOrFakeMultiplayer)
            return null;

        var currentRoom = runState.CurrentRoom;
        if (currentRoom == null)
            return null;

        if (currentRoom is not CombatRoom combatRoom || combatRoom.IsPreFinished)
            return null;

        var roomSnapshot = currentRoom.ToSerializable();
        var runSnapshot = runManager.ToSave(null);
        var stepKey = BuildStepKey(runState, currentRoom);
        var roomScopeKey = BuildRoomScopeKey(runState, currentRoom);
        var roundNumber = combatRoom.CombatState.RoundNumber;

        return new StepSnapshotPayload
        {
            StepKey = stepKey,
            RoomScopeKey = roomScopeKey,
            FloorIndex = runState.TotalFloor,
            RoundNumber = roundNumber,
            CaptureTicksUtc = DateTime.UtcNow.Ticks,
            RoomType = currentRoom.RoomType.ToString(),
            RunJson = SaveManager.ToJson(runSnapshot),
            RoomJson = JsonSerializer.Serialize(roomSnapshot, JsonOptions),
            CombatStateJson = SingleplayerCombatStateSnapshotService.CaptureCurrentCombatStateJson(runState) ?? string.Empty
        };
    }

    private static void ClearSnapshotsIfScopeChanged(string roomScopeKey)
    {
        var latestSnapshot = GetSnapshotFiles().LastOrDefault();
        if (latestSnapshot.FullName == null)
            return;

        var latestSnapshotJson = File.ReadAllText(latestSnapshot.FullName, Encoding.UTF8);
        var payload = JsonSerializer.Deserialize<StepSnapshotPayload>(latestSnapshotJson, JsonOptions);
        if (payload == null)
            return;

        if (payload.RoomScopeKey == roomScopeKey)
            return;

        ClearSnapshots($"switching previous-step scope from {payload.RoomScopeKey} to {roomScopeKey}");
    }

    private static string BuildStepKey(RunState runState, AbstractRoom currentRoom)
    {
        var roomIdentity = currentRoom.ModelId?.ToString() ?? currentRoom.RoomType.ToString();
        var roundNumber = currentRoom is CombatRoom combatRoom ? combatRoom.CombatState.RoundNumber : 0;
        return $"{runState.TotalFloor:D4}_{roundNumber:D4}_{SanitizeFilePart(roomIdentity)}";
    }

    private static string BuildRoomScopeKey(RunState runState, AbstractRoom currentRoom)
    {
        var roomIdentity = currentRoom.ModelId?.ToString() ?? currentRoom.RoomType.ToString();
        return $"{runState.TotalFloor:D4}_{SanitizeFilePart(roomIdentity)}";
    }

    private static string GetSnapshotFileName(StepSnapshotPayload payload)
    {
        return $"step_{payload.FloorIndex:D4}_{payload.RoundNumber:D4}_{payload.CaptureTicksUtc}{SnapshotFileExtension}";
    }

    private static List<SnapshotFile> GetSnapshotFiles()
    {
        var snapshotDirectory = ProjectSettings.GlobalizePath(GetSnapshotDirectoryPath());
        if (!Directory.Exists(snapshotDirectory))
            return [];

        return Directory.GetFiles(snapshotDirectory, $"step_*{SnapshotFileExtension}", SearchOption.TopDirectoryOnly)
            .Select(path => new SnapshotFile(path, File.GetLastWriteTimeUtc(path)))
            .OrderBy(file => file.LastWriteTimeUtc)
            .ToList();
    }

    private static SnapshotFile? GetLatestSnapshotFile()
    {
        var snapshotFiles = GetSnapshotFiles();
        return snapshotFiles.Count == 0 ? null : snapshotFiles[^1];
    }

    private static SnapshotFile? TryGetSnapshotFileForRound(int roundNumber)
    {
        foreach (var snapshotFile in GetSnapshotFiles().OrderByDescending(file => file.LastWriteTimeUtc))
        {
            var snapshotJson = File.ReadAllText(snapshotFile.FullName, Encoding.UTF8);
            var payload = JsonSerializer.Deserialize<StepSnapshotPayload>(snapshotJson, JsonOptions);
            if (payload?.RoundNumber == roundNumber)
                return snapshotFile;
        }

        return null;
    }

    private static RestoredStepSnapshot ReadSnapshot(string snapshotPath)
    {
        var snapshotJson = File.ReadAllText(snapshotPath, Encoding.UTF8);
        var payload = JsonSerializer.Deserialize<StepSnapshotPayload>(snapshotJson, JsonOptions);
        if (payload == null)
            throw new InvalidDataException($"Failed to parse previous step snapshot payload: {snapshotPath}");

        var runReadResult = SaveManager.FromJson<SerializableRun>(payload.RunJson);
        if (!runReadResult.Success || runReadResult.SaveData == null)
            throw new InvalidDataException($"Failed to parse previous step run snapshot: {snapshotPath}");

        var roomSnapshot = JsonSerializer.Deserialize<SerializableRoom>(payload.RoomJson, JsonOptions);
        if (roomSnapshot == null)
            throw new InvalidDataException($"Failed to parse previous step room snapshot: {snapshotPath}");

        return new RestoredStepSnapshot(runReadResult.SaveData, roomSnapshot, payload.StepKey, payload.CombatStateJson);
    }

    private static string? ReadStepKey(string snapshotPath)
    {
        try
        {
            var snapshotJson = File.ReadAllText(snapshotPath, Encoding.UTF8);
            var payload = JsonSerializer.Deserialize<StepSnapshotPayload>(snapshotJson, JsonOptions);
            return payload?.StepKey;
        }
        catch
        {
            return null;
        }
    }

    private static string SanitizeFilePart(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray());
        return sanitized.Replace(':', '_');
    }

    public readonly record struct RestoredStepSnapshot(SerializableRun RunSnapshot, SerializableRoom RoomSnapshot, string StepKey, string CombatStateJson);

    private sealed class StepSnapshotPayload
    {
        public string StepKey { get; set; } = string.Empty;
        public string RoomScopeKey { get; set; } = string.Empty;
        public int FloorIndex { get; set; }
        public int RoundNumber { get; set; }
        public long CaptureTicksUtc { get; set; }
        public string RoomType { get; set; } = string.Empty;
        public string RunJson { get; set; } = string.Empty;
        public string RoomJson { get; set; } = string.Empty;
        public string CombatStateJson { get; set; } = string.Empty;
    }

    private readonly record struct SnapshotFile(string FullName, DateTime LastWriteTimeUtc);
}
