using HarmonyLib;
using HandsUp.HandsUpCode.Services;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Nodes.Combat;
using System.Threading;
using System.Threading.Tasks;

namespace HandsUp.HandsUpCode.Patches;

[HarmonyPatch(typeof(NCombatStartBanner), nameof(NCombatStartBanner.Create))]
public static class SuppressCombatStartBannerForSingleplayerPreviousStepPatch
{
    public static bool Prefix(ref NCombatStartBanner? __result)
    {
        if (!SingleplayerPreviousStepBannerSuppressionService.TryConsumeBannerSuppression())
            return true;

        MainFile.Logger.Info("Suppressed combat start banner during singleplayer previous-step restore.");
        __result = null;
        return false;
    }
}

[HarmonyPatch(typeof(Cmd), nameof(Cmd.CustomScaledWait))]
public static class SuppressCombatStartWaitForSingleplayerPreviousStepPatch
{
    public static bool Prefix(
        float fastSeconds,
        float standardSeconds,
        bool ignoreCombatEnd,
        CancellationToken cancellationToken,
        ref Task __result)
    {
        if (ignoreCombatEnd)
            return true;

        if (fastSeconds == 0.5f && standardSeconds == 1f)
        {
            if (!SingleplayerPreviousStepBannerSuppressionService.TryConsumeWaitSuppression())
                return true;

            MainFile.Logger.Info("Skipped combat start wait during singleplayer previous-step restore.");
            __result = Task.CompletedTask;
            return false;
        }

        if (fastSeconds == 0.5f && standardSeconds == 0.8f)
        {
            if (!SingleplayerPreviousStepBannerSuppressionService.TryConsumePlayerTurnStartWaitSuppression())
                return true;

            MainFile.Logger.Info("Skipped player turn start wait during singleplayer previous-step restore.");
            __result = Task.CompletedTask;
            return false;
        }

        return true;
    }
}
