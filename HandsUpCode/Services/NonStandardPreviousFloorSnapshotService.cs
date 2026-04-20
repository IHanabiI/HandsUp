using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Godot;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Managers;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace HandsUp.HandsUpCode.Services;

public static class NonStandardPreviousFloorSnapshotService
{
    private const string SnapshotRootDirectoryName = "handsup_nonstandard_previous_floor_history";
    private const string SnapshotFileExtension = ".json";

    public static void CaptureSnapshotFromCurrentState(RunManager? runManager)
    {
        if (!NonStandardSingleplayerRunIdentity.TryCreateScope(runManager, out var scope))
            return;

        var runState = runManager?.DebugOnlyGetState();
        if (runState == null)
            return;

        var floorIndex = runState.TotalFloor;
        var snapshotDirectory = ProjectSettings.GlobalizePath(GetSnapshotDirectoryPath(scope));
        Directory.CreateDirectory(snapshotDirectory);

        var snapshot = runManager!.ToSave(null);
        var snapshotJson = SaveManager.ToJson(snapshot);
        var snapshotPath = Path.Combine(snapshotDirectory, GetSnapshotFileName(floorIndex));
        File.WriteAllText(snapshotPath, snapshotJson, Encoding.UTF8);

        MainFile.Logger.Info($"Non-standard previous-floor snapshot written to {snapshotPath}");
    }

    public static bool HasSnapshot(RunManager? runManager)
    {
        if (!NonStandardSingleplayerRunIdentity.TryCreateScope(runManager, out var scope))
            return false;

        return GetSnapshotFiles(scope).Count > 0;
    }

    public static SerializableRun RestoreLatestSnapshot(RunManager? runManager)
    {
        if (!NonStandardSingleplayerRunIdentity.TryCreateScope(runManager, out var scope))
            throw new FileNotFoundException("Non-standard previous floor snapshot scope was not found.");

        var latestSnapshot = GetLatestSnapshotFile(scope)
                             ?? throw new FileNotFoundException("Non-standard previous floor snapshot was not found.");
        var snapshot = ReadSnapshot(latestSnapshot.FullName);

        File.Delete(latestSnapshot.FullName);

        MainFile.Logger.Info($"Non-standard previous-floor snapshot restored from {latestSnapshot.FullName}");
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

    private static string GetSnapshotFileName(int floorIndex)
    {
        return $"floor_{floorIndex:D4}{SnapshotFileExtension}";
    }

    private static List<SnapshotFile> GetSnapshotFiles(NonStandardSingleplayerRunIdentity.SnapshotScope scope)
    {
        var snapshotDirectory = ProjectSettings.GlobalizePath(GetSnapshotDirectoryPath(scope));
        if (!Directory.Exists(snapshotDirectory))
            return [];

        return Directory.GetFiles(snapshotDirectory, $"floor_*{SnapshotFileExtension}", SearchOption.TopDirectoryOnly)
            .Select(path => new SnapshotFile(path, ParseFloorIndex(Path.GetFileNameWithoutExtension(path))))
            .Where(file => file.FloorIndex >= 0)
            .OrderBy(file => file.FloorIndex)
            .ToList();
    }

    private static SnapshotFile? GetLatestSnapshotFile(NonStandardSingleplayerRunIdentity.SnapshotScope scope)
    {
        var snapshotFiles = GetSnapshotFiles(scope);
        return snapshotFiles.Count == 0 ? null : snapshotFiles[^1];
    }

    private static SerializableRun ReadSnapshot(string snapshotPath)
    {
        var snapshotJson = File.ReadAllText(snapshotPath, Encoding.UTF8);
        var readResult = SaveManager.FromJson<SerializableRun>(snapshotJson);
        if (!readResult.Success || readResult.SaveData == null)
            throw new InvalidDataException($"Failed to parse non-standard previous floor snapshot: {snapshotPath}");

        return readResult.SaveData;
    }

    private static int ParseFloorIndex(string fileNameWithoutExtension)
    {
        var underscoreIndex = fileNameWithoutExtension.LastIndexOf('_');
        if (underscoreIndex < 0)
            return -1;

        var floorPart = fileNameWithoutExtension[(underscoreIndex + 1)..];
        return int.TryParse(floorPart, out var floorIndex) ? floorIndex : -1;
    }

    private readonly record struct SnapshotFile(string FullName, int FloorIndex);
}
