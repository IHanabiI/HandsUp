using Godot;
using HarmonyLib;
using BaseLib.Config;
using HandsUp.HandsUpCode.Config;
using MegaCrit.Sts2.Core.Modding;
using HandsUp.HandsUpCode.Services;

namespace HandsUp.HandsUpCode;

//You're recommended but not required to keep all your code in this package and all your assets in the HandsUp folder.
[ModInitializer(nameof(Initialize))]
public partial class MainFile : Node
{
    public const string ModId = "HandsUp"; //At the moment, this is used only for the Logger and harmony names.

    public static MegaCrit.Sts2.Core.Logging.Logger Logger { get; } = new(ModId, MegaCrit.Sts2.Core.Logging.LogType.Generic);

    public static void Initialize()
    {
        Harmony harmony = new(ModId);

        var saveSyncSummary = SaveSyncService.SyncBaseSavesToModded();
        ModConfigRegistry.Register(ModId, new HandsUpModConfig());
        MultiplayerApprovalService.RegisterActionExecutor(MultiplayerExecutionService.ExecuteApprovedActionAsync);
        harmony.PatchAll();
        Logger.Info("HandsUp initialized.");
        Logger.Info(saveSyncSummary);
    }
}
