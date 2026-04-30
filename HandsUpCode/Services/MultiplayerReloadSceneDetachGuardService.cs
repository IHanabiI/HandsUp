using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Nodes;

namespace HandsUp.HandsUpCode.Services;

public static class MultiplayerReloadSceneDetachGuardService
{
    private static readonly object SyncRoot = new();
    private static readonly HashSet<ulong> SuppressedRunInstanceIds = [];

    public static void ArmForDetachedRun(NRun run, string reason)
    {
        if (!GodotObject.IsInstanceValid(run))
            return;

        var sceneInstanceId = run.GetInstanceId();
        lock (SyncRoot)
            SuppressedRunInstanceIds.Add(sceneInstanceId);

        MainFile.Logger.Info(
            $"Armed detached multiplayer run cleanup suppression for scene {sceneInstanceId} ({reason}).");
    }

    public static bool ShouldSuppressCleanup(NRun run)
    {
        if (!GodotObject.IsInstanceValid(run))
            return false;

        lock (SyncRoot)
            return SuppressedRunInstanceIds.Contains(run.GetInstanceId());
    }

    public static void Release(ulong sceneInstanceId, string reason)
    {
        lock (SyncRoot)
        {
            if (!SuppressedRunInstanceIds.Remove(sceneInstanceId))
                return;
        }

        MainFile.Logger.Info(
            $"Released detached multiplayer run cleanup suppression for scene {sceneInstanceId} ({reason}).");
    }
}
