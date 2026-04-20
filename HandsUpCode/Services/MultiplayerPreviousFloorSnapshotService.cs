using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Godot;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Managers;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace HandsUp.HandsUpCode.Services;

public static class MultiplayerPreviousFloorSnapshotService
{
    private const string SnapshotDirectoryName = "handsup_multiplayer_previous_floor_history";
    private const string SnapshotFileExtension = ".json";

    private static string GetSnapshotDirectoryPath()
    {
        return UserDataPathProvider.GetProfileScopedPath(
            SaveManager.Instance.CurrentProfileId,
            Path.Combine(UserDataPathProvider.SavesDir, SnapshotDirectoryName));
    }

    public static void CaptureSnapshotFromCurrentState(RunManager? runManager)
    {
        var runState = runManager?.DebugOnlyGetState();
        var netType = runManager?.NetService?.Type;
        if (runState == null || (netType != NetGameType.Host && netType != NetGameType.Client))
            return;

        var floorIndex = runState.TotalFloor;
        var snapshotDirectory = ProjectSettings.GlobalizePath(GetSnapshotDirectoryPath());
        Directory.CreateDirectory(snapshotDirectory);

        var snapshot = runManager!.ToSave(null);
        var snapshotJson = SaveManager.ToJson(snapshot);
        var snapshotPath = Path.Combine(snapshotDirectory, GetSnapshotFileName(floorIndex));
        File.WriteAllText(snapshotPath, snapshotJson, Encoding.UTF8);

        MainFile.Logger.Info($"Multiplayer previous-floor snapshot written to {snapshotPath}");
    }

    public static bool HasSnapshot()
    {
        return GetSnapshotFiles().Count > 0;
    }

    public static void ClearSnapshots(string reason)
    {
        var snapshotDirectory = ProjectSettings.GlobalizePath(GetSnapshotDirectoryPath());
        if (!Directory.Exists(snapshotDirectory))
            return;

        foreach (var snapshotFile in Directory.GetFiles(snapshotDirectory, $"floor_*{SnapshotFileExtension}", SearchOption.TopDirectoryOnly))
            File.Delete(snapshotFile);

        MainFile.Logger.Info($"Cleared multiplayer previous-floor snapshots: {reason}");
    }

    public static SerializableRun RestoreLatestSnapshot()
    {
        var latestSnapshot = GetLatestSnapshotFile()
                             ?? throw new FileNotFoundException("Multiplayer previous floor snapshot was not found.");
        var snapshot = ReadSnapshot(latestSnapshot.FullName);

        File.Delete(latestSnapshot.FullName);

        MainFile.Logger.Info($"Multiplayer previous-floor snapshot restored from {latestSnapshot.FullName}");
        return snapshot;
    }

    public static SerializableRun PeekLatestSnapshot()
    {
        var latestSnapshot = GetLatestSnapshotFile()
                             ?? throw new FileNotFoundException("Multiplayer previous floor snapshot was not found.");
        return ReadSnapshot(latestSnapshot.FullName);
    }

    public static void DiscardLatestSnapshot(string reason)
    {
        var latestSnapshot = GetLatestSnapshotFile();
        if (latestSnapshot == null)
            return;

        File.Delete(latestSnapshot.Value.FullName);
        MainFile.Logger.Info($"Discarded multiplayer previous-floor snapshot {latestSnapshot.Value.FullName}: {reason}");
    }

    private static string GetSnapshotFileName(int floorIndex)
    {
        return $"floor_{floorIndex:D4}{SnapshotFileExtension}";
    }

    private static List<SnapshotFile> GetSnapshotFiles()
    {
        var snapshotDirectory = ProjectSettings.GlobalizePath(GetSnapshotDirectoryPath());
        if (!Directory.Exists(snapshotDirectory))
            return [];

        return Directory.GetFiles(snapshotDirectory, $"floor_*{SnapshotFileExtension}", SearchOption.TopDirectoryOnly)
            .Select(path => new SnapshotFile(path, ParseFloorIndex(Path.GetFileNameWithoutExtension(path))))
            .Where(file => file.FloorIndex >= 0)
            .OrderBy(file => file.FloorIndex)
            .ToList();
    }

    private static SnapshotFile? GetLatestSnapshotFile()
    {
        var snapshotFiles = GetSnapshotFiles();
        return snapshotFiles.Count == 0 ? null : snapshotFiles[^1];
    }

    private static SerializableRun ReadSnapshot(string snapshotPath)
    {
        var snapshotJson = File.ReadAllText(snapshotPath, Encoding.UTF8);
        var readResult = SaveManager.FromJson<SerializableRun>(snapshotJson);
        if (!readResult.Success || readResult.SaveData == null)
            throw new InvalidDataException($"Failed to parse multiplayer previous floor snapshot: {snapshotPath}");

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
