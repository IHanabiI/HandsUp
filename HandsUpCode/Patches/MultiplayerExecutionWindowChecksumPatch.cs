using HarmonyLib;
using HandsUp.HandsUpCode.Services;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Game.Checksums;

namespace HandsUp.HandsUpCode.Patches;

[HarmonyPatch(typeof(ChecksumTracker), "OnReceivedChecksumDataMessage")]
public static class SuppressIncomingChecksumDataDuringMultiplayerExecuteWindowPatch
{
    public static bool Prefix(ChecksumDataMessage message, ulong senderId)
    {
        if (!MultiplayerExecutionWindowService.ShouldSuppressIncomingChecksums)
            return true;

        MainFile.Logger.Info(
            $"Suppressed incoming multiplayer checksum {message.checksumData.id} from {senderId} during execute window. " +
            MultiplayerExecutionWindowService.DescribeCurrentState());
        return false;
    }
}

[HarmonyPatch(typeof(ChecksumTracker), "OnReceivedStateDivergenceMessage")]
public static class SuppressStateDivergenceDuringMultiplayerExecuteWindowPatch
{
    public static bool Prefix(StateDivergenceMessage message, ulong senderId)
    {
        if (!MultiplayerExecutionWindowService.ShouldSuppressIncomingChecksums)
            return true;

        MainFile.Logger.Warn(
            $"Suppressed incoming multiplayer state divergence {message.senderChecksum.id} from {senderId} during execute window. " +
            MultiplayerExecutionWindowService.DescribeCurrentState());
        return false;
    }
}
