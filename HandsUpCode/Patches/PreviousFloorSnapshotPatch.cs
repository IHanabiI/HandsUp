using HarmonyLib;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Runs;
using HandsUp.HandsUpCode.Services;

namespace HandsUp.HandsUpCode.Patches;

[HarmonyPatch(typeof(RunManager), nameof(RunManager.EnterMapCoord))]
public static class PreviousFloorSnapshotPatch
{
    public static void Prefix(RunManager __instance, MapCoord coord)
    {
        if (!__instance.IsSinglePlayerOrFakeMultiplayer)
            return;

        try
        {
            if (NonStandardSingleplayerRunIdentity.ShouldUseIsolatedSnapshots(__instance))
                NonStandardPreviousFloorSnapshotService.CaptureSnapshotFromCurrentState(__instance);
            else
                PreviousFloorSnapshotService.CaptureSnapshotFromCurrentState(__instance);

            MainFile.Logger.Info($"Captured previous-floor snapshot before entering map coord {coord}.");
        }
        catch (System.Exception e)
        {
            MainFile.Logger.Error($"Failed to capture previous-floor snapshot before entering map coord {coord}: {e}");
        }
    }
}
