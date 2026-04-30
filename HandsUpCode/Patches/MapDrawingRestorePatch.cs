using System;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.MapDrawing;

namespace HandsUp.HandsUpCode.Patches;

[HarmonyPatch(typeof(NMapScreen), nameof(NMapScreen.Open))]
public static class MapDrawingRestorePatch
{
    public static void Postfix()
    {
        try
        {
            if (RunManager.Instance?.MapDrawingsToLoad == null)
                return;

            TaskHelper.RunSafely(RestorePendingMapDrawingsAsync(RunManager.Instance.MapDrawingsToLoad));
            RunManager.Instance.MapDrawingsToLoad = null;
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"Failed to schedule pending map drawing restore: {e}");
        }
    }

    private static async System.Threading.Tasks.Task RestorePendingMapDrawingsAsync(SerializableMapDrawings mapDrawings)
    {
        if (NGame.Instance == null)
            return;

        await NGame.Instance.ToSignal(NGame.Instance.GetTree(), SceneTree.SignalName.ProcessFrame);
        await NGame.Instance.ToSignal(NGame.Instance.GetTree(), SceneTree.SignalName.ProcessFrame);

        var drawings = NMapScreen.Instance?.Drawings;
        if (drawings == null)
        {
            MainFile.Logger.Warn("Skipped pending map drawing restore because NMapScreen.Drawings was unavailable.");
            return;
        }

        drawings.ClearAllLines();
        drawings.LoadDrawings(mapDrawings);

        MainFile.Logger.Info($"Applied pending map drawing restore on map screen open. PlayerDrawingSets={mapDrawings.drawings.Count}.");
    }
}

[HarmonyPatch(typeof(RunManager), nameof(RunManager.EnterAct))]
public static class ClearPendingMapDrawingRestoreOnActTransitionPatch
{
    public static void Prefix(int currentActIndex)
    {
        try
        {
            if (RunManager.Instance?.MapDrawingsToLoad == null)
                return;

            MainFile.Logger.Info(
                $"Cleared pending map drawing restore before act transition to act {currentActIndex} to preserve vanilla drawing reset behavior.");
            RunManager.Instance.MapDrawingsToLoad = null;
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"Failed to clear pending map drawing restore before act transition: {e}");
        }
    }
}
