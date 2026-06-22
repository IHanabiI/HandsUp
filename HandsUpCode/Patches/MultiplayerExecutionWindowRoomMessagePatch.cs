using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using HandsUp.HandsUpCode.Services;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Game;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Game.Sync;

namespace HandsUp.HandsUpCode.Patches;

internal static class MultiplayerExecutionWindowRoomMessagePatchHelper
{
    public static bool ShouldProcess(string messageKind, object[] args)
    {
        if (!MultiplayerExecutionWindowService.HasActiveExecutionWindow)
            return true;

        var message = args.Length > 0
            ? args[0]
            : "<missing>";
        var senderId = args.Length > 1 && args[1] is ulong ulongSenderId
            ? ulongSenderId
            : 0UL;

        MainFile.Logger.Info(
            $"Suppressed incoming multiplayer {messageKind} from {senderId} during execute window: {message}. " +
            MultiplayerExecutionWindowService.DescribeCurrentState());
        return false;
    }

    public static bool ShouldProcessCombatSync(MethodBase originalMethod, object[] args)
    {
        if (!MultiplayerExecutionWindowService.HasActiveExecutionWindow)
            return true;

        var message = args.Length > 0
            ? args[0]
            : "<missing>";
        var senderId = args.Length > 1 && args[1] is ulong ulongSenderId
            ? ulongSenderId
            : 0UL;

        if (MultiplayerExecutionWindowService.ShouldAllowCriticalCombatSyncMessages
            && IsCriticalCombatSyncMessage(message))
        {
            MainFile.Logger.Info(
                $"Allowed incoming multiplayer combat sync {originalMethod.Name} from {senderId} during execute window because critical startup sync is temporarily allowed: {message}. " +
                MultiplayerExecutionWindowService.DescribeCurrentState());
            return true;
        }

        MainFile.Logger.Info(
            $"Suppressed incoming multiplayer combat sync {originalMethod.Name} from {senderId} during execute window: {message}. " +
            MultiplayerExecutionWindowService.DescribeCurrentState());
        return false;
    }

    private static bool IsCriticalCombatSyncMessage(object? message)
    {
        return message is SyncPlayerDataMessage or SyncRngMessage;
    }
}

[HarmonyPatch(typeof(ActionQueueSynchronizer), "HandleRequestEnqueueActionMessage")]
public static class SuppressRequestEnqueueActionDuringMultiplayerExecuteWindowPatch
{
    public static bool Prefix(object[] __args)
    {
        return MultiplayerExecutionWindowRoomMessagePatchHelper.ShouldProcess(
            "action enqueue request",
            __args);
    }
}

[HarmonyPatch(typeof(ActionQueueSynchronizer), "HandleActionEnqueuedMessage")]
public static class SuppressActionEnqueuedDuringMultiplayerExecuteWindowPatch
{
    public static bool Prefix(object[] __args)
    {
        return MultiplayerExecutionWindowRoomMessagePatchHelper.ShouldProcess(
            "action enqueue broadcast",
            __args);
    }
}

[HarmonyPatch(typeof(ActionQueueSynchronizer), "HandleRequestEnqueueHookActionMessage")]
public static class SuppressRequestEnqueueHookActionDuringMultiplayerExecuteWindowPatch
{
    public static bool Prefix(object[] __args)
    {
        return MultiplayerExecutionWindowRoomMessagePatchHelper.ShouldProcess(
            "hook enqueue request",
            __args);
    }
}

[HarmonyPatch(typeof(ActionQueueSynchronizer), "HandleHookActionEnqueuedMessage")]
public static class SuppressHookActionEnqueuedDuringMultiplayerExecuteWindowPatch
{
    public static bool Prefix(object[] __args)
    {
        return MultiplayerExecutionWindowRoomMessagePatchHelper.ShouldProcess(
            "hook enqueue broadcast",
            __args);
    }
}

[HarmonyPatch(typeof(ActionQueueSynchronizer), "HandleRequestResumeActionAfterPlayerChoiceMessage")]
public static class SuppressRequestResumeActionAfterPlayerChoiceDuringMultiplayerExecuteWindowPatch
{
    public static bool Prefix(object[] __args)
    {
        return MultiplayerExecutionWindowRoomMessagePatchHelper.ShouldProcess(
            "resume-action request",
            __args);
    }
}

[HarmonyPatch(typeof(ActionQueueSynchronizer), "HandleResumeActionAfterPlayerChoiceMessage")]
public static class SuppressResumeActionAfterPlayerChoiceDuringMultiplayerExecuteWindowPatch
{
    public static bool Prefix(object[] __args)
    {
        return MultiplayerExecutionWindowRoomMessagePatchHelper.ShouldProcess(
            "resume-action broadcast",
            __args);
    }
}

[HarmonyPatch(typeof(EventSynchronizer), "HandleVotedForSharedEventOptionMessage")]
public static class SuppressSharedEventVoteDuringMultiplayerExecuteWindowPatch
{
    public static bool Prefix(object[] __args)
    {
        return MultiplayerExecutionWindowRoomMessagePatchHelper.ShouldProcess(
            "shared-event vote",
            __args);
    }
}

[HarmonyPatch(typeof(EventSynchronizer), "HandleSharedEventOptionChosenMessage")]
public static class SuppressSharedEventOptionChosenDuringMultiplayerExecuteWindowPatch
{
    public static bool Prefix(object[] __args)
    {
        return MultiplayerExecutionWindowRoomMessagePatchHelper.ShouldProcess(
            "shared-event result",
            __args);
    }
}

[HarmonyPatch(typeof(EventSynchronizer), "HandleEventOptionChosenMessage")]
public static class SuppressEventOptionChosenDuringMultiplayerExecuteWindowPatch
{
    public static bool Prefix(object[] __args)
    {
        return MultiplayerExecutionWindowRoomMessagePatchHelper.ShouldProcess(
            "event option",
            __args);
    }
}

[HarmonyPatch(typeof(RewardSynchronizer), "HandleRewardObtainedMessage")]
public static class SuppressRewardObtainedDuringMultiplayerExecuteWindowPatch
{
    public static bool Prefix(object[] __args)
    {
        return MultiplayerExecutionWindowRoomMessagePatchHelper.ShouldProcess(
            "reward sync",
            __args);
    }
}

[HarmonyPatch(typeof(RewardSynchronizer), "HandleGoldLostMessage")]
public static class SuppressGoldLostDuringMultiplayerExecuteWindowPatch
{
    public static bool Prefix(object[] __args)
    {
        return MultiplayerExecutionWindowRoomMessagePatchHelper.ShouldProcess(
            "gold-loss sync",
            __args);
    }
}

[HarmonyPatch(typeof(RewardSynchronizer), "HandleCardRemovedMessage")]
public static class SuppressCardRemovedDuringMultiplayerExecuteWindowPatch
{
    public static bool Prefix(object[] __args)
    {
        return MultiplayerExecutionWindowRoomMessagePatchHelper.ShouldProcess(
            "card-removal sync",
            __args);
    }
}

public static class SuppressPaelsWingSacrificeDuringMultiplayerExecuteWindowPatch
{
    public static bool Prefix(object[] __args)
    {
        return MultiplayerExecutionWindowRoomMessagePatchHelper.ShouldProcess(
            "paels-wing sacrifice sync",
            __args);
    }
}

[HarmonyPatch]
public static class SuppressCombatStateSyncDuringMultiplayerExecuteWindowPatch
{
    public static IEnumerable<MethodBase> TargetMethods()
    {
        var combatStateSynchronizerType = ResolveCombatStateSynchronizerType();
        if (combatStateSynchronizerType == null)
        {
            MainFile.Logger.Warn("Skipped registering multiplayer combat sync execute-window suppression because CombatStateSynchronizer could not be resolved.");
            return Enumerable.Empty<MethodBase>();
        }

        return AccessTools
            .GetDeclaredMethods(combatStateSynchronizerType)
            .Where(IsIncomingCombatSyncHandler);
    }

    public static bool Prefix(MethodBase __originalMethod, object[] __args)
    {
        return MultiplayerExecutionWindowRoomMessagePatchHelper.ShouldProcessCombatSync(
            __originalMethod,
            __args);
    }

    private static bool IsIncomingCombatSyncHandler(MethodInfo method)
    {
        if (method.ReturnType != typeof(void))
            return false;

        var parameters = method.GetParameters();
        if (parameters.Length != 2 || parameters[1].ParameterType != typeof(ulong))
            return false;

        var firstParameterType = parameters[0].ParameterType;
        if (!IsGameMessageType(firstParameterType))
            return false;

        return method.Name.Contains("sync", StringComparison.OrdinalIgnoreCase)
               || firstParameterType.Name.Contains("sync", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsGameMessageType(Type type)
    {
        return type.Namespace?.Contains("Multiplayer.Messages.Game", StringComparison.Ordinal) == true;
    }

    private static Type? ResolveCombatStateSynchronizerType()
    {
        return AppDomain.CurrentDomain
            .GetAssemblies()
            .SelectMany(SafelyGetTypes)
            .FirstOrDefault(type => string.Equals(type.Name, "CombatStateSynchronizer", StringComparison.Ordinal));
    }

    private static IEnumerable<Type> SafelyGetTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException e)
        {
            return e.Types.Where(type => type != null)!;
        }
    }
}
