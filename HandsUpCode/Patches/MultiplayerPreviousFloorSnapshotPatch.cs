using HarmonyLib;
using HandsUp.HandsUpCode.Services;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;

namespace HandsUp.HandsUpCode.Patches;

[HarmonyPatch(typeof(RunManager), nameof(RunManager.EnterMapCoord))]
public static class MultiplayerPreviousFloorSnapshotPatch
{
    public static void Prefix(RunManager __instance, MapCoord coord)
    {
        var netType = __instance.NetService?.Type;
        if (netType != NetGameType.Host && netType != NetGameType.Client)
            return;

        try
        {
            MultiplayerPreviousFloorSnapshotService.CaptureSnapshotFromCurrentState(__instance);
            MainFile.Logger.Info($"Captured multiplayer previous-floor snapshot before entering map coord {coord}.");
        }
        catch (System.Exception e)
        {
            MainFile.Logger.Error($"Failed to capture multiplayer previous-floor snapshot before entering map coord {coord}: {e}");
        }
    }
}
