using System.IO;
using System.Text;
using Godot;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Managers;

namespace HandsUp.HandsUpCode.Services;

public static class SoftRestartSnapshotService
{
    private const string SnapshotFileName = "handsup_soft_restart_snapshot.json";

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
        if (runState == null || currentRoom == null || currentRoom is MapRoom)
            return;

        var snapshotDirectory = Path.GetDirectoryName(ProjectSettings.GlobalizePath(GetSnapshotPath()));
        if (!string.IsNullOrWhiteSpace(snapshotDirectory))
            Directory.CreateDirectory(snapshotDirectory);

        var snapshot = runManager!.ToSave(currentRoom);
        var snapshotJson = SaveManager.ToJson(snapshot);
        var snapshotPath = ProjectSettings.GlobalizePath(GetSnapshotPath());
        File.WriteAllText(snapshotPath, snapshotJson, Encoding.UTF8);

        MainFile.Logger.Info($"Soft-restart snapshot written from {sourceTag} to {snapshotPath}");
    }

    public static bool HasSnapshot()
    {
        return File.Exists(ProjectSettings.GlobalizePath(GetSnapshotPath()));
    }

    public static SerializableRun? TryReadSnapshot()
    {
        var snapshotPath = ProjectSettings.GlobalizePath(GetSnapshotPath());
        if (!File.Exists(snapshotPath))
            return null;

        var snapshotJson = File.ReadAllText(snapshotPath, Encoding.UTF8);
        var readResult = SaveManager.FromJson<SerializableRun>(snapshotJson);
        if (!readResult.Success || readResult.SaveData == null)
        {
            MainFile.Logger.Warn($"Failed to parse soft-restart snapshot: {snapshotPath}");
            return null;
        }

        return readResult.SaveData;
    }
}
