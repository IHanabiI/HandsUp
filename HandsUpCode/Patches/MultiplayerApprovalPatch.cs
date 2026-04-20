using HarmonyLib;
using HandsUp.HandsUpCode.Services;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;

namespace HandsUp.HandsUpCode.Patches;

[HarmonyPatch(typeof(RunManager), nameof(RunManager.InitializeRunLobby))]
public static class RegisterMultiplayerApprovalHandlersPatch
{
    public static void Postfix(INetGameService netService, RunState state)
    {
        MultiplayerApprovalService.RegisterIfNeeded(netService, state);
    }
}

[HarmonyPatch(typeof(RunManager), nameof(RunManager.CleanUp))]
public static class UnregisterMultiplayerApprovalHandlersPatch
{
    public static void Prefix()
    {
        MultiplayerApprovalService.Unregister();
    }
}
