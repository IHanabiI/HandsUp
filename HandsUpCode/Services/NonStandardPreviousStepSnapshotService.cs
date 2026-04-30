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

public static class NonStandardPreviousStepSnapshotService
{
    private const string SnapshotRootDirectoryName = "handsup_nonstandard_previous_step_history";
    private const string SnapshotFileExtension = ".json";
    private static string? _suppressedNextCaptureKey;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    public static void CaptureSnapshotFromCurrentCombatStep(RunManager? runManager, string sourceTag)
    {
        try
        {
            if (!NonStandardSingleplayerRunIdentity.TryCreateScope(runManager, out var scope))
            {
                MainFile.Logger.Info($"Skipped non-standard previous-step snapshot capture from {sourceTag} because snapshot scope was unavailable.");
                return;
            }

            var payload = BuildSnapshotPayload(runManager);
            if (payload == null)
            {
                MainFile.Logger.Info($"Skipped non-standard previous-step snapshot capture from {sourceTag} because snapshot payload was unavailable.");
                return;
            }

            ClearSnapshotsIfScopeChanged(scope, payload.RoomScopeKey);
            WriteSnapshotPayload(scope, payload, sourceTag);
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"Failed to capture non-standard previous-step snapshot from {sourceTag}: {e}");
        }
    }

    public static void ClearSnapshots(RunManager? runManager, string reason)
    {
        if (!NonStandardSingleplayerRunIdentity.TryCreateScope(runManager, out var scope))
            return;

        var snapshotDirectory = ProjectSettings.GlobalizePath(GetSnapshotDirectoryPath(scope));
        if (!Directory.Exists(snapshotDirectory))
            return;

        foreach (var snapshotFile in Directory.GetFiles(snapshotDirectory, $"step_*{SnapshotFileExtension}", SearchOption.TopDirectoryOnly))
            File.Delete(snapshotFile);

        _suppressedNextCaptureKey = null;
        MainFile.Logger.Info($"Cleared non-standard previous-step snapshots: {reason}");
    }

    public static bool HasSnapshot(RunManager? runManager)
    {
        if (!NonStandardSingleplayerRunIdentity.TryCreateScope(runManager, out var scope))
            return false;

        return GetSnapshotFiles(scope).Count > 0;
    }

    public static bool HasSnapshotForRound(RunManager? runManager, int roundNumber)
    {
        if (!NonStandardSingleplayerRunIdentity.TryCreateScope(runManager, out var scope))
            return false;

        return TryGetSnapshotFileForRound(scope, roundNumber) != null;
    }

    public static PreviousStepSnapshotService.RestoredStepSnapshot RestoreSnapshotForRound(RunManager? runManager, int roundNumber)
    {
        if (!NonStandardSingleplayerRunIdentity.TryCreateScope(runManager, out var scope))
            throw new FileNotFoundException("Non-standard previous-step snapshot scope was not found.");

        var snapshotFile = TryGetSnapshotFileForRound(scope, roundNumber)
                           ?? throw new FileNotFoundException($"Non-standard previous step snapshot for round {roundNumber} was not found.");
        var snapshot = ReadSnapshot(snapshotFile.FullName);

        _suppressedNextCaptureKey = snapshot.StepKey;
        MainFile.Logger.Info($"Non-standard previous-step snapshot for round {roundNumber} restored from {snapshotFile.FullName} without deleting the snapshot node.");
        return snapshot;
    }

    private static string GetSnapshotDirectoryPath(NonStandardSingleplayerRunIdentity.SnapshotScope scope)
    {
        return UserDataPathProvider.GetProfileScopedPath(
            SaveManager.Instance.CurrentProfileId,
            Path.Combine(
                UserDataPathProvider.SavesDir,
                SnapshotRootDirectoryName,
                NonStandardSingleplayerRunIdentity.GetModeDirectoryName(scope.Mode),
                scope.RunKey));
    }

    private static void WriteSnapshotPayload(NonStandardSingleplayerRunIdentity.SnapshotScope scope, StepSnapshotPayload payload, string sourceTag)
    {
        if (_suppressedNextCaptureKey == payload.StepKey)
        {
            MainFile.Logger.Info($"Skipped non-standard previous-step snapshot capture for {payload.StepKey} after restore.");
            _suppressedNextCaptureKey = null;
            return;
        }

        var existingSnapshotForSameStep = GetSnapshotFiles(scope)
            .Where(snapshotFile => ReadStepKey(snapshotFile.FullName) == payload.StepKey)
            .ToList();

        foreach (var snapshotFile in existingSnapshotForSameStep)
            File.Delete(snapshotFile.FullName);

        if (existingSnapshotForSameStep.Count > 0)
            MainFile.Logger.Info($"Overwriting non-standard previous-step snapshot node for {payload.StepKey}.");

        var snapshotDirectory = ProjectSettings.GlobalizePath(GetSnapshotDirectoryPath(scope));
        Directory.CreateDirectory(snapshotDirectory);

        var snapshotPath = Path.Combine(snapshotDirectory, GetSnapshotFileName(payload));
        var snapshotJson = JsonSerializer.Serialize(payload, JsonOptions);
        File.WriteAllText(snapshotPath, snapshotJson, Encoding.UTF8);

        MainFile.Logger.Info($"Non-standard previous-step snapshot written from {sourceTag} to {snapshotPath}");
    }

    private static StepSnapshotPayload? BuildSnapshotPayload(RunManager? runManager)
    {
        var runState = runManager?.DebugOnlyGetState();
        if (runState == null || !runManager!.IsSinglePlayerOrFakeMultiplayer)
            return null;

        if (runState.CurrentRoom is not CombatRoom combatRoom || combatRoom.IsPreFinished)
            return null;

        var roomSnapshot = SingleplayerEventCombatPreviousStepService.CreateRoomSnapshot(runState.CurrentRoom);
        var runSnapshot = runManager.ToSave(null);
        var stepKey = BuildStepKey(runState, runState.CurrentRoom);
        var roomScopeKey = BuildRoomScopeKey(runState, runState.CurrentRoom);

        return new StepSnapshotPayload
        {
            StepKey = stepKey,
            RoomScopeKey = roomScopeKey,
            FloorIndex = runState.TotalFloor,
            RoundNumber = combatRoom.CombatState.RoundNumber,
            CaptureTicksUtc = DateTime.UtcNow.Ticks,
            RoomType = runState.CurrentRoom.RoomType.ToString(),
            RunJson = SaveManager.ToJson(runSnapshot),
            RoomJson = JsonSerializer.Serialize(roomSnapshot, JsonOptions),
            CombatStateJson = SingleplayerCombatStateSnapshotService.CaptureCurrentCombatStateJson(runState) ?? string.Empty
        };
    }

    private static void ClearSnapshotsIfScopeChanged(NonStandardSingleplayerRunIdentity.SnapshotScope scope, string roomScopeKey)
    {
        var latestSnapshot = GetSnapshotFiles(scope).LastOrDefault();
        if (latestSnapshot.FullName == null)
            return;

        var latestSnapshotJson = File.ReadAllText(latestSnapshot.FullName, Encoding.UTF8);
        var payload = JsonSerializer.Deserialize<StepSnapshotPayload>(latestSnapshotJson, JsonOptions);
        if (payload == null || payload.RoomScopeKey == roomScopeKey)
            return;

        ClearSnapshotsInScope(scope, $"switching non-standard previous-step scope from {payload.RoomScopeKey} to {roomScopeKey}");
    }

    private static void ClearSnapshotsInScope(NonStandardSingleplayerRunIdentity.SnapshotScope scope, string reason)
    {
        var snapshotDirectory = ProjectSettings.GlobalizePath(GetSnapshotDirectoryPath(scope));
        if (!Directory.Exists(snapshotDirectory))
            return;

        foreach (var snapshotFile in Directory.GetFiles(snapshotDirectory, $"step_*{SnapshotFileExtension}", SearchOption.TopDirectoryOnly))
            File.Delete(snapshotFile);

        _suppressedNextCaptureKey = null;
        MainFile.Logger.Info($"Cleared non-standard previous-step snapshots: {reason}");
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

    private static List<SnapshotFile> GetSnapshotFiles(NonStandardSingleplayerRunIdentity.SnapshotScope scope)
    {
        var snapshotDirectory = ProjectSettings.GlobalizePath(GetSnapshotDirectoryPath(scope));
        if (!Directory.Exists(snapshotDirectory))
            return [];

        return Directory.GetFiles(snapshotDirectory, $"step_*{SnapshotFileExtension}", SearchOption.TopDirectoryOnly)
            .Select(path => new SnapshotFile(path, File.GetLastWriteTimeUtc(path)))
            .OrderBy(file => file.LastWriteTimeUtc)
            .ToList();
    }

    private static SnapshotFile? TryGetSnapshotFileForRound(NonStandardSingleplayerRunIdentity.SnapshotScope scope, int roundNumber)
    {
        foreach (var snapshotFile in GetSnapshotFiles(scope).OrderByDescending(file => file.LastWriteTimeUtc))
        {
            var snapshotJson = File.ReadAllText(snapshotFile.FullName, Encoding.UTF8);
            var payload = JsonSerializer.Deserialize<StepSnapshotPayload>(snapshotJson, JsonOptions);
            if (payload?.RoundNumber == roundNumber)
                return snapshotFile;
        }

        return null;
    }

    private static PreviousStepSnapshotService.RestoredStepSnapshot ReadSnapshot(string snapshotPath)
    {
        var snapshotJson = File.ReadAllText(snapshotPath, Encoding.UTF8);
        var payload = JsonSerializer.Deserialize<StepSnapshotPayload>(snapshotJson, JsonOptions);
        if (payload == null)
            throw new InvalidDataException($"Failed to parse non-standard previous step snapshot payload: {snapshotPath}");

        var runReadResult = SaveManager.FromJson<SerializableRun>(payload.RunJson);
        if (!runReadResult.Success || runReadResult.SaveData == null)
            throw new InvalidDataException($"Failed to parse non-standard previous step run snapshot: {snapshotPath}");

        var roomSnapshot = JsonSerializer.Deserialize<SerializableRoom>(payload.RoomJson, JsonOptions);
        if (roomSnapshot == null)
            throw new InvalidDataException($"Failed to parse non-standard previous step room snapshot: {snapshotPath}");

        return new PreviousStepSnapshotService.RestoredStepSnapshot(runReadResult.SaveData, roomSnapshot, payload.StepKey, payload.CombatStateJson);
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
