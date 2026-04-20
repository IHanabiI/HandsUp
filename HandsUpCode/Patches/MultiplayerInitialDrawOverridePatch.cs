using HarmonyLib;
using HandsUp.HandsUpCode.Services;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;

namespace HandsUp.HandsUpCode.Patches;

[HarmonyPatch(typeof(CardPileCmd), nameof(CardPileCmd.Draw), typeof(PlayerChoiceContext), typeof(decimal), typeof(Player), typeof(bool))]
public static class MultiplayerInitialDrawOverridePatch
{
    public static void Prefix(Player player, bool fromHandDraw)
    {
        try
        {
            MultiplayerInitialCombatPileSnapshotService.ApplyPendingInitialDrawOverrideIfNeeded(player, fromHandDraw);
        }
        catch (System.Exception e)
        {
            MainFile.Logger.Error($"Failed to apply multiplayer initial draw override: {e}");
        }
    }
}
