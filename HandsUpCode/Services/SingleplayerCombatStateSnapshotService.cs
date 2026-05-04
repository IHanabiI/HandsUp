using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Animation;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Orbs;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Orbs;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Multiplayer.Replay;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Orbs;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.Nodes.Vfx.Backgrounds;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace HandsUp.HandsUpCode.Services;

public static class SingleplayerCombatStateSnapshotService
{
    private const int CombatReadyTimeoutMs = 5000;
    private const int CombatReadyPollIntervalMs = 15;
    private const int FollowUpUiRefreshDelayMs = 25;
    private const string IllusionReviveMoveId = "REVIVE_MOVE";
    private const string DoormakerDramaticOpenMoveId = "DRAMATIC_OPEN_MOVE";
    private const string DoormakerHungerMoveId = "HUNGER_MOVE";
    private const string DoormakerScrutinyMoveId = "SCRUTINY_MOVE";
    private const string DoormakerGraspMoveId = "GRASP_MOVE";
    private const string DoormakerFaceVisualPath = "monsters/beta/door_maker_placeholder_2.png";
    private const string DoormakerTeethVisualPath = "monsters/beta/door_maker_placeholder_3.png";
    private const string DoormakerHandsVisualPath = "monsters/beta/door_maker_placeholder_4.png";
    private const string OwlMagistrateVerdictMoveId = "VERDICT";
    private const string OwlMagistrateTakeOffTrigger = "TakeOff";
    private const string OwlMagistrateFlyLoopAnimationId = "fly_loop";
    private const string OwlMagistrateFlyingBoundsContainer = "FlyingBounds";
    private const string RocketLaserMoveId = "LASER_MOVE";
    private const string RocketRechargeMoveId = "RECHARGE_MOVE";
    private const string KnowledgeDemonPonderMoveId = "PONDER_MOVE";
    private const string KnowledgeDemonIdleLoopAnimationId = "idle_loop";
    private const string KnowledgeDemonBurntLoopAnimationId = "burnt_loop";
    private const string KnowledgeDemonBurningStartMethod = "OnBurningStart";
    private const string KnowledgeDemonBurningEndMethod = "OnBurningEnd";
    private const string TestSubjectRespawnMoveId = "RESPAWN_MOVE";
    private const string TestSubjectIdleLoop1AnimationId = "idle_loop1";
    private const string TestSubjectIdleLoop2AnimationId = "idle_loop2";
    private const string TestSubjectIdleLoop3AnimationId = "idle_loop3";
    private const string TestSubjectKnockedOutLoop1AnimationId = "knocked_out_loop1";
    private const string TestSubjectKnockedOutLoop2AnimationId = "knocked_out_loop2";
    private const string TestSubjectDieAnimationId = "die";
    private const int KaiserCrabRightArmTrack = 2;

    private static readonly MethodInfo? CombatStateAddCardMethod = typeof(CombatState)
        .GetMethod("AddCard", BindingFlags.Instance | BindingFlags.NonPublic, [typeof(CardModel)]);
    private static readonly MethodInfo? HandOnCombatStateChangedMethod = typeof(NPlayerHand)
        .GetMethod("OnCombatStateChanged", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly MethodInfo? HandCanPlayCardsMethod = typeof(NPlayerHand)
        .GetMethod("CanPlayCards", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly MethodInfo? HandAreCardActionsAllowedMethod = typeof(NPlayerHand)
        .GetMethod("AreCardActionsAllowed", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly MethodInfo? EndTurnButtonOnTurnStartedMethod = typeof(NEndTurnButton)
        .GetMethod("OnTurnStarted", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? NCardPlayQueueItemsField = typeof(NCardPlayQueue)
        .GetField("_playQueue", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly Type? NCardPlayQueueItemType = typeof(NCardPlayQueue)
        .GetNestedType("QueueItem", BindingFlags.NonPublic);
    private static readonly FieldInfo? NCardPlayQueueItemCardField = NCardPlayQueueItemType
        ?.GetField("card", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    private static readonly FieldInfo? NCardPlayQueueItemCurrentTweenField = NCardPlayQueueItemType
        ?.GetField("currentTween", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    private static readonly FieldInfo? CombatCardPileCurrentCountField = typeof(NCombatCardPile)
        .GetField("_currentCount", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? CombatCardPileCountLabelField = typeof(NCombatCardPile)
        .GetField("_countLabel", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly MethodInfo? MonsterModelNextMoveSetter = typeof(MonsterModel)
        .GetProperty("NextMove", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?
        .GetSetMethod(true);
    private static readonly FieldInfo? MoveStateMachineCurrentStateField = typeof(MonsterMoveStateMachine)
        .GetField("_currentState", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? MoveStateMachinePerformedFirstMoveField = typeof(MonsterMoveStateMachine)
        .GetField("_performedFirstMove", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly MethodInfo? CombatReplayWriterRecordInitialStateMethod = typeof(CombatReplayWriter)
        .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        .FirstOrDefault(method => method.Name == "RecordInitialState" && method.GetParameters().Length == 1);
    private static readonly MethodInfo? SandpitPowerUpdateCreaturePositionsMethod = typeof(SandpitPower)
        .GetMethod("UpdateCreaturePositions", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly MethodInfo? SurroundedPowerFaceDirectionMethod = typeof(SurroundedPower)
        .GetMethod("FaceDirection", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly MethodInfo? DoormakerUpdateVisualMethod = typeof(Doormaker)
        .GetMethod("UpdateVisual", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? NCreatureSpineAnimatorField = typeof(NCreature)
        .GetField("_spineAnimator", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? CreatureAnimatorCurrentStateField = typeof(CreatureAnimator)
        .GetField("_currentState", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly PropertyInfo? NCombatRoomEncounterSlotsProperty = typeof(NCombatRoom)
        .GetProperty("EncounterSlots", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
    private static readonly FieldInfo? NCombatRoomCreatureNodesField = typeof(NCombatRoom)
        .GetField("_creatureNodes", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? NCombatRoomRemovingCreatureNodesField = typeof(NCombatRoom)
        .GetField("_removingCreatureNodes", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? NOrbManagerOrbContainerField = typeof(NOrbManager)
        .GetField("_orbContainer", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? NOrbManagerOrbsField = typeof(NOrbManager)
        .GetField("_orbs", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? NOrbManagerCurrentTweenField = typeof(NOrbManager)
        .GetField("_curTween", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly MethodInfo? NOrbManagerTweenLayoutMethod = typeof(NOrbManager)
        .GetMethod("TweenLayout", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly MethodInfo? NOrbManagerUpdateControllerNavigationMethod = typeof(NOrbManager)
        .GetMethod("UpdateControllerNavigation", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? CombatManagerPlayerActionsDisabledField = typeof(CombatManager)
        .GetField("_playerActionsDisabled", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? CombatManagerPlayersReadyToEndTurnField = typeof(CombatManager)
        .GetField("_playersReadyToEndTurn", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? CombatManagerPlayersReadyToBeginEnemyTurnField = typeof(CombatManager)
        .GetField("_playersReadyToBeginEnemyTurn", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? KaiserCrabRightArmStateField = typeof(NKaiserCrabBossBackground)
        .GetField("_rightArmState", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly ModelId StrengthPowerId = ModelDb.Power<StrengthPower>().Id;
    private static readonly ModelId CrabRagePowerId = ModelDb.Power<CrabRagePower>().Id;
    private static readonly ModelId SurroundedPowerId = ModelDb.Power<SurroundedPower>().Id;
    private static readonly PropertyInfo? CombatManagerIsPlayPhaseProperty = typeof(CombatManager)
        .GetProperty("IsPlayPhase", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    private static readonly PropertyInfo? CombatManagerIsEnemyTurnStartedProperty = typeof(CombatManager)
        .GetProperty("IsEnemyTurnStarted", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    private static readonly PropertyInfo? CombatManagerEndingPlayerTurnPhaseOneProperty = typeof(CombatManager)
        .GetProperty("EndingPlayerTurnPhaseOne", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    private static readonly PropertyInfo? CombatManagerEndingPlayerTurnPhaseTwoProperty = typeof(CombatManager)
        .GetProperty("EndingPlayerTurnPhaseTwo", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    private static readonly FieldInfo? CombatManagerIsPlayPhaseField = typeof(CombatManager)
        .GetField("<IsPlayPhase>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? typeof(CombatManager).GetField("_isPlayPhase", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? CombatManagerIsEnemyTurnStartedField = typeof(CombatManager)
        .GetField("<IsEnemyTurnStarted>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? typeof(CombatManager).GetField("_isEnemyTurnStarted", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? CombatManagerEndingPlayerTurnPhaseOneField = typeof(CombatManager)
        .GetField("<EndingPlayerTurnPhaseOne>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? typeof(CombatManager).GetField("_endingPlayerTurnPhaseOne", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? CombatManagerEndingPlayerTurnPhaseTwoField = typeof(CombatManager)
        .GetField("<EndingPlayerTurnPhaseTwo>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? typeof(CombatManager).GetField("_endingPlayerTurnPhaseTwo", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? DarkOrbEvokeValueField = typeof(DarkOrb)
        .GetField("_evokeVal", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? GlassOrbPassiveValueField = typeof(GlassOrb)
        .GetField("_passiveVal", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    public static string? CaptureCurrentCombatStateJson(RunState? runState)
    {
        if (runState?.CurrentRoom is not CombatRoom combatRoom || combatRoom.IsPreFinished)
            return null;

        var snapshot = CombatStateSnapshot.FromCurrentRun(runState);
        return JsonSerializer.Serialize(snapshot, JsonOptions);
    }

    public static async Task<bool> TryApplyCombatStateJsonAsync(string? combatStateJson, RunState runState, string restoreContext)
    {
        if (string.IsNullOrWhiteSpace(combatStateJson))
            return false;

        CombatStateSnapshot? snapshot;
        try
        {
            snapshot = JsonSerializer.Deserialize<CombatStateSnapshot>(combatStateJson, JsonOptions);
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"Failed to parse singleplayer combat snapshot for {restoreContext}: {e.Message}");
            return false;
        }

        if (snapshot == null)
            return false;

        var readyContext = await WaitForCombatReadyAsync(runState, restoreContext);
        if (readyContext == null)
        {
            MainFile.Logger.Warn($"Skipped applying singleplayer combat snapshot for {restoreContext} because combat was not ready.");
            return false;
        }

        var combatState = readyContext.Value.CombatState;
        var combatRoom = readyContext.Value.CombatRoom;

        runState.Rng.LoadFromSerializable(snapshot.Rng);
        RunManager.Instance.PlayerChoiceSynchronizer.FastForwardChoiceIds(snapshot.NextChoiceIds);

        if (snapshot.LastExecutedActionId.HasValue)
            RunManager.Instance.ActionQueueSet.FastForwardNextActionId(snapshot.LastExecutedActionId.Value + 1);

        if (snapshot.LastExecutedHookId.HasValue)
            RunManager.Instance.ActionQueueSynchronizer.FastForwardHookId(snapshot.LastExecutedHookId.Value + 1);

        combatState.RoundNumber = snapshot.RoundNumber;
        combatState.CurrentSide = snapshot.CurrentSide;

        await EnsureSnapshotCreaturesExistAsync(combatState, snapshot, restoreContext);
        var resolutionContext = BuildSnapshotResolutionContext(combatState, snapshot.Creatures);
        RestoreCreatureStates(combatState, snapshot.Creatures, resolutionContext);
        RemoveCreaturesMissingFromSnapshot(combatState, snapshot, restoreContext, resolutionContext);
        RestorePlayerStates(runState, snapshot.Players);
        LogKaiserCrabRocketRestoreDebug(combatState, snapshot.Creatures, restoreContext, "after-restore-creature-states");
        await RestoreCreatureExtrasAsync(combatState, snapshot.Creatures, restoreContext, resolutionContext);
        LogKaiserCrabRocketRestoreDebug(combatState, snapshot.Creatures, restoreContext, "after-restore-creature-extras");
        RestorePowerExtras(runState, combatState, snapshot.Creatures, restoreContext, resolutionContext);
        LogKaiserCrabRocketRestoreDebug(combatState, snapshot.Creatures, restoreContext, "after-restore-power-extras");

        CombatManager.Instance.StateTracker.SetState(combatState);
        RestoreCombatManagerState(snapshot.CombatManager, restoreContext);
        await ReconcileCombatRoomCreatureNodes(combatState, snapshot.Creatures, restoreContext, resolutionContext);
        ReinitializeCombatReplayWriter(restoreContext);
        await SynchronizePostRestoreSpecialPowerStateAsync(combatState, snapshot.Creatures, restoreContext, resolutionContext);
        LogKaiserCrabRocketRestoreDebug(combatState, snapshot.Creatures, restoreContext, "after-post-restore-power-sync");
        await TryRefreshEnemyIntentDisplaysAsync(combatState, restoreContext);
        RestoreHiddenLiveAllyVisuals(combatState, restoreContext);
        SynchronizePlayerOrbManagersForSingleplayerRestore(combatState, restoreContext);

        foreach (var player in combatState.Players)
            player.PlayerCombatState?.RecalculateCardValues();

        TryRefreshLocalCombatUi(runState, combatState, snapshot.RoundNumber, restoreContext);
        await TryRefreshLocalCombatUiAfterDelayAsync(runState, combatRoom, combatState, snapshot.RoundNumber, restoreContext);

        MainFile.Logger.Info($"Applied singleplayer combat snapshot for {restoreContext}.");
        return true;
    }

    private static async Task EnsureSnapshotCreaturesExistAsync(CombatState combatState, CombatStateSnapshot snapshot, string restoreContext)
    {
        var resolvedCreatures = new HashSet<Creature>();
        var recreatedCreatures = new List<string>();

        foreach (var creatureSnapshot in snapshot.Creatures.Where(creature => creature.PlayerId == null))
        {
            var existingCreature = ResolveCreature(combatState, creatureSnapshot, resolvedCreatures);
            if (existingCreature != null)
            {
                resolvedCreatures.Add(existingCreature);
                continue;
            }

            if (string.IsNullOrWhiteSpace(creatureSnapshot.MonsterId))
                continue;

            try
            {
                var monster = SaveUtil.MonsterOrDeprecated(new ModelId("MONSTER", creatureSnapshot.MonsterId)).ToMutable();
                var recreatedCreature = await CreatureCmd.Add(monster, combatState, CombatSide.Enemy, creatureSnapshot.SlotName);
                resolvedCreatures.Add(recreatedCreature);
                recreatedCreatures.Add(DescribeSnapshotCreatureForLog(creatureSnapshot));
            }
            catch (Exception e)
            {
                MainFile.Logger.Warn(
                    $"Failed to recreate creature missing from singleplayer combat snapshot for {restoreContext}. " +
                    $"Monster={creatureSnapshot.MonsterId}, Slot={creatureSnapshot.SlotName ?? "unknown"}: {e.Message}");
            }
        }

        if (recreatedCreatures.Count == 0)
            return;

        if (snapshot.EncounterSlots.Count > 0)
            combatState.SortEnemiesBySlotName();

        MainFile.Logger.Info(
            $"Recreated {recreatedCreatures.Count} creature(s) missing from singleplayer combat snapshot for {restoreContext}: " +
            $"{string.Join(", ", recreatedCreatures)}");
    }

    private static async Task<ReadySingleplayerCombatRestoreContext?> WaitForCombatReadyAsync(RunState runState, string restoreContext)
    {
        if (runState.CurrentRoom is not CombatRoom)
            return null;

        if (TryGetReadyRestoreContext(runState, out var readyContext))
            return readyContext;

        var startedAt = Time.GetTicksMsec();
        while (Time.GetTicksMsec() - startedAt < CombatReadyTimeoutMs)
        {
            await Task.Delay(CombatReadyPollIntervalMs);
            if (TryGetReadyRestoreContext(runState, out readyContext))
            {
                MainFile.Logger.Info($"Singleplayer combat restore became UI-ready after {Time.GetTicksMsec() - startedAt} ms for {restoreContext}.");
                return readyContext;
            }
        }

        MainFile.Logger.Warn($"Timed out while waiting for singleplayer combat UI readiness before applying snapshot for {restoreContext}.");
        return TryGetReadyRestoreContext(runState, out readyContext) ? readyContext : null;
    }

    private static bool TryGetReadyRestoreContext(RunState runState, out ReadySingleplayerCombatRestoreContext readyContext)
    {
        readyContext = default;

        if (runState.CurrentRoom is not CombatRoom combatRoom || combatRoom.IsPreFinished)
            return false;

        var combatState = combatRoom.CombatState;
        if (combatState == null
            || NCombatRoom.Instance == null
            || NPlayerHand.Instance == null
            || NCombatRoom.Instance.Ui?.EndTurnButton == null)
        {
            return false;
        }

        if (!CombatManager.Instance.IsInProgress
            || RunManager.Instance.ActionQueueSynchronizer.CombatState != ActionSynchronizerCombatState.PlayPhase)
        {
            return false;
        }

        readyContext = new ReadySingleplayerCombatRestoreContext(combatRoom, combatState);
        return true;
    }

    private static void RestoreCreatureStates(
        CombatState combatState,
        IReadOnlyList<CombatCreatureSnapshot> snapshots,
        SnapshotResolutionContext resolutionContext)
    {
        foreach (var snapshot in snapshots)
        {
            var creature = ResolveCreature(combatState, snapshot, resolutionContext);
            if (creature == null)
            {
                MainFile.Logger.Warn(
                    $"Skipped singleplayer combat creature snapshot restore because the target creature could not be resolved. " +
                    $"Monster={snapshot.MonsterId}, Index={snapshot.MonsterInstanceIndex}, Slot={snapshot.SlotName}, Player={snapshot.PlayerId}");
                continue;
            }

            if (!string.IsNullOrWhiteSpace(snapshot.SlotName))
                creature.SlotName = snapshot.SlotName;

            creature.SetMaxHpInternal(snapshot.MaxHp);
            creature.SetCurrentHpInternal(snapshot.CurrentHp);
            creature.LoseBlockInternal(creature.Block);
            creature.GainBlockInternal(snapshot.Block);

            creature.RemoveAllPowersInternalExcept(null);
            foreach (var powerSnapshot in snapshot.Powers)
            {
                try
                {
                    var power = ModelDb.GetById<PowerModel>(powerSnapshot.Id).ToMutable();
                    power.ApplyInternal(creature, powerSnapshot.Amount, true);
                }
                catch (Exception e)
                {
                    MainFile.Logger.Warn($"Skipped restoring singleplayer power {powerSnapshot.Id} x{powerSnapshot.Amount} for creature {(snapshot.PlayerId?.ToString() ?? snapshot.MonsterId ?? "unknown")} because restore failed: {e.Message}");
                }
            }
        }
    }

    private static void RemoveCreaturesMissingFromSnapshot(
        CombatState combatState,
        CombatStateSnapshot snapshot,
        string restoreContext,
        SnapshotResolutionContext resolutionContext)
    {
        var snapshotEnemyCount = snapshot.Creatures.Count(creature => creature.PlayerId == null);
        var currentEnemyCreatures = combatState.Creatures
            .Where(creature => creature.Player == null)
            .ToList();

        if (snapshotEnemyCount >= currentEnemyCreatures.Count)
            return;

        var matchedEnemies = ResolveMatchedSnapshotEnemies(combatState, snapshot.Creatures, resolutionContext);
        var matchedEnemySet = matchedEnemies.ToHashSet();
        var extraCreatures = currentEnemyCreatures
            .Where(creature => !matchedEnemySet.Contains(creature))
            .ToList();

        if (extraCreatures.Count == 0)
            return;

        foreach (var creature in extraCreatures)
            RemoveCreatureFromCombatState(combatState, creature, restoreContext);

        if (snapshot.EncounterSlots.Count > 0)
            combatState.SortEnemiesBySlotName();
        MainFile.Logger.Info(
            $"Removed {extraCreatures.Count} creature(s) missing from singleplayer combat snapshot for {restoreContext}: " +
            $"{string.Join(", ", extraCreatures.Select(DescribeCreatureForLog))}");
    }

    private static List<Creature> ResolveMatchedSnapshotEnemies(
        CombatState combatState,
        IReadOnlyList<CombatCreatureSnapshot> snapshots,
        SnapshotResolutionContext resolutionContext)
    {
        var matchedCreatures = new List<Creature>();
        var usedCreatures = new HashSet<Creature>();

        foreach (var snapshot in snapshots.Where(snapshot => snapshot.PlayerId == null))
        {
            var creature = ResolveCreature(combatState, snapshot, resolutionContext, usedCreatures);
            if (creature == null)
                continue;

            matchedCreatures.Add(creature);
            usedCreatures.Add(creature);
        }

        return matchedCreatures;
    }

    private static string DescribeCreatureForLog(Creature creature)
    {
        var monsterId = creature.Monster?.Id.Entry ?? "unknown";
        return string.IsNullOrWhiteSpace(creature.SlotName)
            ? monsterId
            : $"{monsterId}:{creature.SlotName}";
    }

    private static string DescribeSnapshotCreatureForLog(CombatCreatureSnapshot snapshot)
    {
        var monsterId = snapshot.MonsterId ?? "unknown";
        return string.IsNullOrWhiteSpace(snapshot.SlotName)
            ? monsterId
            : $"{monsterId}:{snapshot.SlotName}";
    }

    private static void RemoveCreatureFromCombatState(CombatState combatState, Creature creature, string restoreContext)
    {
        try
        {
            CombatManager.Instance.RemoveCreature(creature);
            if (CombatStateCompatibilityService.BelongsToCombatState(creature, combatState))
                combatState.RemoveCreature(creature, true);
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn(
                $"Failed to fully remove creature during singleplayer combat snapshot restore for {restoreContext}. " +
                $"Monster={creature.Monster?.Id.Entry ?? "unknown"}, Slot={creature.SlotName ?? "unknown"}: {e.Message}");
        }
    }

    private static void RestorePlayerStates(RunState runState, IReadOnlyList<CombatPlayerSnapshot> snapshots)
    {
        foreach (var snapshot in snapshots)
        {
            var player = runState.GetPlayer(snapshot.PlayerId);
            if (player == null)
            {
                MainFile.Logger.Warn($"Skipped singleplayer combat player snapshot restore because player {snapshot.PlayerId} was not found.");
                continue;
            }

            player.PlayerRng.LoadFromSerializable(snapshot.RngSet);
            player.PlayerOdds.LoadFromSerializable(snapshot.OddsSet);
            player.RelicGrabBag.LoadFromSerializable(snapshot.RelicGrabBag);
            player.Gold = snapshot.Gold;

            var combat = player.PlayerCombatState;
            if (combat == null)
                continue;

            combat.Energy = snapshot.Energy;
            combat.Stars = snapshot.Stars;
            RestorePlayerPiles(runState, player, snapshot.Piles);
            RestorePlayerOrbs(player, combat, snapshot.Orbs);
        }
    }

    private static async Task RestoreCreatureExtrasAsync(
        CombatState combatState,
        IReadOnlyList<CombatCreatureSnapshot> snapshots,
        string restoreContext,
        SnapshotResolutionContext resolutionContext)
    {
        foreach (var snapshot in snapshots)
        {
            var creature = ResolveCreature(combatState, snapshot, resolutionContext);
            var monster = creature?.Monster;
            var moveStateMachine = monster?.MoveStateMachine;
            if (monster == null || moveStateMachine == null)
                continue;

            if (snapshot.IsTemporaryStunned)
            {
                await EnsureTemporaryStunnedMoveStateAsync(creature, snapshot, restoreContext);
                moveStateMachine = monster.MoveStateMachine;
                if (moveStateMachine == null)
                    continue;
            }

            foreach (var fieldSnapshot in snapshot.SpecialFields)
            {
                TryRestoreField(monster, fieldSnapshot, $"monster {snapshot.MonsterId}");
            }

            if (await TryRestoreIllusionReviveMoveStateAsync(creature, snapshot, restoreContext))
            {
                await TrySynchronizeSpecialMonsterStateAsync(creature, snapshot, restoreContext, refreshIntentDisplays: false);
                continue;
            }

            if (!string.IsNullOrWhiteSpace(snapshot.CurrentMoveStateId)
                && moveStateMachine.States.TryGetValue(snapshot.CurrentMoveStateId, out var currentState)
                && currentState is MoveState moveState)
            {
                RestoreMonsterMoveStateSilently(monster, moveStateMachine, moveState, snapshot, creature, restoreContext);
            }
            else if (!string.IsNullOrWhiteSpace(snapshot.CurrentMoveStateId))
            {
                MainFile.Logger.Warn(
                    $"Skipped restoring singleplayer move state {snapshot.CurrentMoveStateId} for monster {snapshot.MonsterId} " +
                    $"because it was not present after restore. Slot={snapshot.SlotName}, TemporaryStunned={snapshot.IsTemporaryStunned}");
            }

            await TrySynchronizeSpecialMonsterStateAsync(creature, snapshot, restoreContext, refreshIntentDisplays: false);
        }
    }

    private static void RestoreMonsterMoveStateSilently(
        MonsterModel monster,
        MonsterMoveStateMachine moveStateMachine,
        MoveState moveState,
        CombatCreatureSnapshot snapshot,
        Creature? creature,
        string restoreContext)
    {
        if (MonsterModelNextMoveSetter == null)
        {
            MainFile.Logger.Warn(
                $"Skipped restoring singleplayer move state {snapshot.CurrentMoveStateId ?? "unknown"} for monster {snapshot.MonsterId ?? creature?.Monster?.Id.Entry ?? "unknown"} " +
                $"because NextMove setter could not be reflected during {restoreContext}.");
            return;
        }

        ApplyFollowUpStateSnapshot(moveStateMachine, moveState, snapshot, creature);
        MonsterModelNextMoveSetter.Invoke(monster, [moveState]);

        moveStateMachine.StateLog.Clear();
        foreach (var stateId in snapshot.MoveStateLogIds)
        {
            if (moveStateMachine.States.TryGetValue(stateId, out var loggedState))
                moveStateMachine.StateLog.Add(loggedState);
        }

        MoveStateMachineCurrentStateField?.SetValue(moveStateMachine, moveState);
        MoveStateMachinePerformedFirstMoveField?.SetValue(moveStateMachine, snapshot.PerformedFirstMove);
    }

    private static async Task<bool> TryRestoreIllusionReviveMoveStateAsync(Creature? creature, CombatCreatureSnapshot snapshot, string restoreContext)
    {
        if (creature == null
            || !string.Equals(snapshot.CurrentMoveStateId, IllusionReviveMoveId, StringComparison.Ordinal))
        {
            return false;
        }

        var illusionPower = creature.GetPower<IllusionPower>();
        if (illusionPower == null)
            return false;

        try
        {
            illusionPower.FollowUpStateId = snapshot.CurrentMoveFollowUpStateId;
            await illusionPower.AfterDeath(null!, creature, false, 0f);

            MainFile.Logger.Info(
                $"Recreated illusion revive move state for {restoreContext}. " +
                $"Monster={snapshot.MonsterId}, Slot={snapshot.SlotName}, FollowUp={snapshot.CurrentMoveFollowUpStateId ?? "null"}");
            return true;
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn(
                $"Failed to recreate illusion revive move state for {restoreContext}. " +
                $"Monster={snapshot.MonsterId}, Slot={snapshot.SlotName}, FollowUp={snapshot.CurrentMoveFollowUpStateId ?? "null"}: {e.Message}");
            return false;
        }
    }

    private static async Task TrySynchronizeSpecialMonsterStateAsync(
        Creature? creature,
        CombatCreatureSnapshot snapshot,
        string restoreContext,
        bool refreshIntentDisplays)
    {
        if (creature?.Monster is Rocket)
        {
            await TrySynchronizeKaiserCrabRocketStateAsync(creature, snapshot, restoreContext, refreshIntentDisplays);
            return;
        }

        if (creature?.Monster is TestSubject)
        {
            await TrySynchronizeTestSubjectVisualStateAsync(creature, snapshot, restoreContext, refreshIntentDisplays);
            return;
        }

        if (creature?.Monster is Doormaker doormaker)
        {
            await TrySynchronizeDoormakerVisualStateAsync(creature, doormaker, snapshot, restoreContext, refreshIntentDisplays);
            return;
        }

        if (creature?.Monster is OwlMagistrate)
        {
            await TrySynchronizeOwlMagistrateVisualStateAsync(creature, snapshot, restoreContext, refreshIntentDisplays);
            return;
        }

        if (creature?.Monster is KnowledgeDemon)
        {
            await TrySynchronizeKnowledgeDemonVisualStateAsync(creature, snapshot, restoreContext, refreshIntentDisplays);
            return;
        }

        if (creature?.Monster is not ToughEgg toughEgg)
            return;

        var creatureNode = NCombatRoom.Instance?.GetCreatureNode(creature);
        RepositionToughEggNode(creature, toughEgg, snapshot, creatureNode);

        if (!toughEgg.IsHatched)
        {
            if (refreshIntentDisplays && creatureNode != null)
                await creatureNode.RefreshIntents();
            return;
        }

        try
        {
            await CreatureCmd.TriggerAnim(creature, "Hatch", 0.5f);

            RepositionToughEggNode(creature, toughEgg, snapshot, creatureNode);
            if (refreshIntentDisplays && creatureNode != null)
                await creatureNode.RefreshIntents();

            MainFile.Logger.Info(
                $"Synchronized hatched tough egg visuals during singleplayer combat snapshot restore for {restoreContext}. " +
                $"Slot={snapshot.SlotName ?? "unknown"} Move={snapshot.CurrentMoveStateId ?? "unknown"}");
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn(
                $"Failed to synchronize hatched tough egg visuals during singleplayer combat snapshot restore for {restoreContext}. " +
                $"Slot={snapshot.SlotName ?? "unknown"} Move={snapshot.CurrentMoveStateId ?? "unknown"}: {e.Message}");
        }
    }

    private static async Task TrySynchronizeKnowledgeDemonVisualStateAsync(
        Creature creature,
        CombatCreatureSnapshot snapshot,
        string restoreContext,
        bool refreshIntentDisplays)
    {
        try
        {
            var creatureNode = NCombatRoom.Instance?.GetCreatureNode(creature);
            if (creatureNode == null || !creatureNode.HasSpineAnimation || creature.Monster is not KnowledgeDemon knowledgeDemon)
            {
                MainFile.Logger.Warn(
                    $"Skipped Knowledge Demon visual sync during singleplayer combat snapshot restore for {restoreContext} " +
                    $"because the creature node or spine animation was unavailable. Move={snapshot.CurrentMoveStateId ?? "unknown"}");
                return;
            }

            var isBurnt = TryReadSpecialFieldBool(snapshot.SpecialFields, "_isBurnt", out var restoredBurnt)
                ? restoredBurnt
                : knowledgeDemon.IsBurnt || string.Equals(snapshot.CurrentMoveStateId, KnowledgeDemonPonderMoveId, StringComparison.Ordinal);
            var targetAnimationId = isBurnt
                ? KnowledgeDemonBurntLoopAnimationId
                : KnowledgeDemonIdleLoopAnimationId;

            ForceCreatureLoopAnimation(creatureNode, targetAnimationId);
            SynchronizeKnowledgeDemonBurningVfx(creatureNode, isBurnt);

            if (refreshIntentDisplays)
                await creatureNode.RefreshIntents();

            MainFile.Logger.Info(
                $"Synchronized Knowledge Demon visual state during singleplayer combat snapshot restore for {restoreContext}. " +
                $"Move={snapshot.CurrentMoveStateId ?? "unknown"} Burnt={isBurnt} Anim={targetAnimationId}");
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn(
                $"Failed to synchronize Knowledge Demon visual state during singleplayer combat snapshot restore for {restoreContext}. " +
                $"Move={snapshot.CurrentMoveStateId ?? "unknown"}: {e.Message}");
        }
    }

    private static void ForceCreatureLoopAnimation(NCreature creatureNode, string animationId)
    {
        if (NCreatureSpineAnimatorField?.GetValue(creatureNode) is CreatureAnimator spineAnimator
            && CreatureAnimatorCurrentStateField != null)
        {
            CreatureAnimatorCurrentStateField.SetValue(spineAnimator, new AnimState(animationId, true));
        }

        creatureNode.SpineAnimation.SetAnimation(animationId, true, 0);
    }

    private static void SynchronizeKnowledgeDemonBurningVfx(NCreature creatureNode, bool isBurnt)
    {
        var vfxNode = FindKnowledgeDemonVfxNode(creatureNode);
        if (vfxNode == null)
            return;

        vfxNode.Call(isBurnt ? KnowledgeDemonBurningStartMethod : KnowledgeDemonBurningEndMethod);
    }

    private static NKnowledgeDemonVfx? FindKnowledgeDemonVfxNode(Node root)
    {
        foreach (var child in root.GetChildren())
        {
            if (child is NKnowledgeDemonVfx vfxNode)
                return vfxNode;

            if (child is Node childNode)
            {
                var nested = FindKnowledgeDemonVfxNode(childNode);
                if (nested != null)
                    return nested;
            }
        }

        return null;
    }

    private static async Task TrySynchronizeKaiserCrabRocketStateAsync(
        Creature creature,
        CombatCreatureSnapshot snapshot,
        string restoreContext,
        bool refreshIntentDisplays)
    {
        try
        {
            SynchronizeKaiserCrabRocketPowerState(creature, snapshot);
            TrySynchronizeKaiserCrabRocketVisualState(snapshot, restoreContext);

            var creatureNode = NCombatRoom.Instance?.GetCreatureNode(creature);
            if (refreshIntentDisplays && creatureNode != null)
                await creatureNode.RefreshIntents();

            LogKaiserCrabRocketRestoreDebug(
                CombatStateCompatibilityService.GetCombatState(creature),
                [],
                restoreContext,
                "rocket-sync-before-power-extras");

            MainFile.Logger.Info(
                $"Synchronized Kaiser Crab rocket combat state during singleplayer combat snapshot restore for {restoreContext}. " +
                $"Slot={snapshot.SlotName ?? "unknown"} Move={snapshot.CurrentMoveStateId ?? "unknown"} " +
                $"Strength={creature.GetPower<StrengthPower>()?.Amount ?? 0} " +
                $"CrabRage={(creature.GetPower<CrabRagePower>() != null)}");
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn(
                $"Failed to synchronize Kaiser Crab rocket combat state during singleplayer combat snapshot restore for {restoreContext}. " +
                $"Slot={snapshot.SlotName ?? "unknown"} Move={snapshot.CurrentMoveStateId ?? "unknown"}: {e.Message}");
        }
    }

    private static async Task TrySynchronizeDoormakerVisualStateAsync(
        Creature creature,
        Doormaker doormaker,
        CombatCreatureSnapshot snapshot,
        string restoreContext,
        bool refreshIntentDisplays)
    {
        try
        {
            var creatureNode = NCombatRoom.Instance?.GetCreatureNode(creature);
            if (creatureNode == null)
            {
                MainFile.Logger.Warn(
                    $"Skipped Doormaker visual sync during singleplayer combat snapshot restore for {restoreContext} " +
                    $"because the creature node was unavailable. Move={snapshot.CurrentMoveStateId ?? "unknown"}");
                return;
            }

            var isPortalOpen = TryReadSpecialFieldBool(snapshot.SpecialFields, "_isPortalOpen", out var restoredPortalOpen)
                ? restoredPortalOpen
                : ResolveDoormakerPortalOpenFallback(creature, snapshot);

            creature.ShowsInfiniteHp = !isPortalOpen;

            if (isPortalOpen)
            {
                var visualPath = ResolveDoormakerVisualPath(creature, snapshot);
                if (!string.IsNullOrWhiteSpace(visualPath))
                {
                    if (DoormakerUpdateVisualMethod != null)
                    {
                        DoormakerUpdateVisualMethod.Invoke(doormaker, new object?[] { visualPath });
                    }
                    else
                    {
                        MainFile.Logger.Warn(
                            $"Skipped Doormaker visual texture sync during singleplayer combat snapshot restore for {restoreContext} " +
                            $"because UpdateVisual could not be reflected. Move={snapshot.CurrentMoveStateId ?? "unknown"}");
                    }
                }
            }

            creatureNode.GetNodeOrNull<Node>("%HealthBar")?.Call("RefreshValues");
            if (refreshIntentDisplays)
                await creatureNode.RefreshIntents();

            MainFile.Logger.Info(
                $"Synchronized Doormaker phase state during singleplayer combat snapshot restore for {restoreContext}. " +
                $"Move={snapshot.CurrentMoveStateId ?? "unknown"} PortalOpen={isPortalOpen} " +
                $"Hunger={(creature.GetPower<HungerPower>() != null)} " +
                $"Scrutiny={(creature.GetPower<ScrutinyPower>() != null)} " +
                $"Grasp={(creature.GetPower<GraspPower>() != null)} " +
                $"InfiniteHp={creature.ShowsInfiniteHp}");
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn(
                $"Failed to synchronize Doormaker phase state during singleplayer combat snapshot restore for {restoreContext}. " +
                $"Move={snapshot.CurrentMoveStateId ?? "unknown"}: {e.Message}");
        }
    }

    private static bool ResolveDoormakerPortalOpenFallback(Creature creature, CombatCreatureSnapshot snapshot)
    {
        if (creature.GetPower<HungerPower>() != null
            || creature.GetPower<ScrutinyPower>() != null
            || creature.GetPower<GraspPower>() != null)
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(snapshot.CurrentMoveStateId)
            && !string.Equals(snapshot.CurrentMoveStateId, DoormakerDramaticOpenMoveId, StringComparison.Ordinal);
    }

    private static string? ResolveDoormakerVisualPath(Creature creature, CombatCreatureSnapshot snapshot)
    {
        if (creature.GetPower<GraspPower>() != null
            || string.Equals(snapshot.CurrentMoveStateId, DoormakerGraspMoveId, StringComparison.Ordinal))
        {
            return DoormakerHandsVisualPath;
        }

        if (creature.GetPower<ScrutinyPower>() != null
            || string.Equals(snapshot.CurrentMoveStateId, DoormakerScrutinyMoveId, StringComparison.Ordinal))
        {
            return DoormakerFaceVisualPath;
        }

        if (creature.GetPower<HungerPower>() != null
            || string.Equals(snapshot.CurrentMoveStateId, DoormakerHungerMoveId, StringComparison.Ordinal))
        {
            return DoormakerTeethVisualPath;
        }

        return null;
    }

    private static async Task TrySynchronizeOwlMagistrateVisualStateAsync(
        Creature creature,
        CombatCreatureSnapshot snapshot,
        string restoreContext,
        bool refreshIntentDisplays)
    {
        try
        {
            var creatureNode = NCombatRoom.Instance?.GetCreatureNode(creature);
            if (creatureNode == null)
            {
                MainFile.Logger.Warn(
                    $"Skipped Owl Magistrate visual sync during singleplayer combat snapshot restore for {restoreContext} " +
                    $"because the creature node was unavailable. Move={snapshot.CurrentMoveStateId ?? "unknown"}");
                return;
            }

            var isFlying = TryReadSpecialFieldBool(snapshot.SpecialFields, "_isFlying", out var restoredFlying)
                ? restoredFlying
                : ResolveOwlMagistrateFlyingFallback(creature, snapshot);

            if (isFlying)
            {
                creatureNode.Call("SetAnimationTrigger", OwlMagistrateTakeOffTrigger);
                if (!TryForceOwlMagistrateFlyLoop(creatureNode))
                {
                    creatureNode.SpineAnimation.SetAnimation(OwlMagistrateFlyLoopAnimationId, true, 0);
                    creatureNode.Call("UpdateBounds", OwlMagistrateFlyingBoundsContainer);
                }
            }

            creatureNode.GetNodeOrNull<Node>("%HealthBar")?.Call("RefreshValues");
            if (refreshIntentDisplays)
                await creatureNode.RefreshIntents();

            MainFile.Logger.Info(
                $"Synchronized Owl Magistrate flying state during singleplayer combat snapshot restore for {restoreContext}. " +
                $"Move={snapshot.CurrentMoveStateId ?? "unknown"} Flying={isFlying} " +
                $"Soar={(creature.GetPower<SoarPower>() != null)}");
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn(
                $"Failed to synchronize Owl Magistrate flying state during singleplayer combat snapshot restore for {restoreContext}. " +
                $"Move={snapshot.CurrentMoveStateId ?? "unknown"}: {e.Message}");
        }
    }

    private static bool ResolveOwlMagistrateFlyingFallback(Creature creature, CombatCreatureSnapshot snapshot)
    {
        if (creature.GetPower<SoarPower>() != null)
            return true;

        return string.Equals(snapshot.CurrentMoveStateId, OwlMagistrateVerdictMoveId, StringComparison.Ordinal);
    }

    private static bool TryForceOwlMagistrateFlyLoop(NCreature creatureNode)
    {
        if (NCreatureSpineAnimatorField?.GetValue(creatureNode) is not CreatureAnimator spineAnimator
            || CreatureAnimatorCurrentStateField == null)
        {
            return false;
        }

        if (CreatureAnimatorCurrentStateField.GetValue(spineAnimator) is not AnimState currentState)
            return false;

        var flyLoopState = currentState.NextState;
        if (flyLoopState == null
            || !string.Equals(flyLoopState.Id, OwlMagistrateFlyLoopAnimationId, StringComparison.Ordinal))
        {
            return false;
        }

        CreatureAnimatorCurrentStateField.SetValue(spineAnimator, flyLoopState);
        creatureNode.SpineAnimation.SetAnimation(flyLoopState.Id, flyLoopState.IsLooping, 0);

        if (!string.IsNullOrWhiteSpace(flyLoopState.BoundsContainer))
            creatureNode.Call("UpdateBounds", flyLoopState.BoundsContainer);

        return true;
    }

    private static void SynchronizeKaiserCrabRocketPowerState(Creature creature, CombatCreatureSnapshot snapshot)
    {
        SynchronizeSnapshotPowerAmount<StrengthPower>(creature, snapshot, StrengthPowerId);
        SynchronizeSnapshotPowerAmount<CrabRagePower>(creature, snapshot, CrabRagePowerId);
    }

    private static void SynchronizeSnapshotPowerAmount<TPower>(Creature creature, CombatCreatureSnapshot snapshot, ModelId powerId)
        where TPower : PowerModel
    {
        var desiredAmount = snapshot.Powers
            .Where(power => power.Id == powerId)
            .Select(power => power.Amount)
            .DefaultIfEmpty(0)
            .Last();

        var existingPower = creature.GetPower<TPower>();
        var existingAmount = existingPower?.Amount ?? 0;
        if (existingAmount == desiredAmount)
            return;

        existingPower?.RemoveInternal();
        if (desiredAmount == 0)
            return;

        var restoredPower = ModelDb.Power<TPower>().ToMutable();
        restoredPower.ApplyInternal(creature, desiredAmount, true);
    }

    private static void LogKaiserCrabRocketRestoreDebug(
        CombatState? combatState,
        IReadOnlyList<CombatCreatureSnapshot> snapshots,
        string restoreContext,
        string phase)
    {
        if (combatState == null)
            return;

        var rocket = combatState.Creatures.FirstOrDefault(creature => creature.Monster is Rocket);
        if (rocket == null)
            return;

        try
        {
            var player = LocalContext.GetMe(combatState)?.Creature ?? combatState.Players.FirstOrDefault()?.Creature;
            var liveFacing = player?.GetPower<SurroundedPower>()?.Facing.ToString() ?? "none";
            var snapshotFacing = TryGetSnapshotSurroundedFacing(snapshots, out var facing)
                ? facing.ToString()
                : "missing";
            var attackIntent = rocket.Monster?.NextMove?.Intents?.OfType<AttackIntent>().FirstOrDefault();
            var targets = combatState.Players
                .Select(playerState => playerState.Creature)
                .Where(creature => creature != null)
                .Cast<Creature>()
                .ToList();
            var singleDamage = attackIntent?.GetSingleDamage(targets, rocket);
            var totalDamage = attackIntent?.GetTotalDamage(targets, rocket);
            var attackTier = totalDamage.HasValue ? ResolveAttackIntentTier(totalDamage.Value).ToString() : "none";

            MainFile.Logger.Info(
                $"[HandsUp][rocket-debug] phase={phase} restore={restoreContext} " +
                $"move={rocket.Monster?.NextMove?.Id ?? "none"} " +
                $"snapshotFacing={snapshotFacing} liveFacing={liveFacing} " +
                $"strength={rocket.GetPower<StrengthPower>()?.Amount ?? 0} " +
                $"crabRage={(rocket.GetPower<CrabRagePower>() != null)} " +
                $"singleDamage={singleDamage?.ToString() ?? "none"} " +
                $"totalDamage={totalDamage?.ToString() ?? "none"} " +
                $"tier={attackTier}");
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"[HandsUp][rocket-debug] Failed to log Kaiser Crab rocket restore debug for {restoreContext} at {phase}: {e.Message}");
        }
    }

    private static bool TryGetSnapshotSurroundedFacing(
        IReadOnlyList<CombatCreatureSnapshot> snapshots,
        out SurroundedPower.Direction facing)
    {
        facing = default;

        var fieldSnapshot = snapshots
            .Where(snapshot => snapshot.PlayerId.HasValue)
            .SelectMany(snapshot => snapshot.Powers)
            .FirstOrDefault(powerSnapshot => powerSnapshot.Id == SurroundedPowerId)?
            .SpecialFields
            .FirstOrDefault(field => string.Equals(field.FieldName, "_facing", StringComparison.Ordinal));
        if (fieldSnapshot == null)
            return false;

        try
        {
            facing = JsonSerializer.Deserialize<SurroundedPower.Direction>(fieldSnapshot.ValueJson);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static int ResolveAttackIntentTier(int totalDamage)
    {
        return totalDamage switch
        {
            < 5 => 1,
            < 10 => 2,
            < 20 => 3,
            < 40 => 4,
            _ => 5
        };
    }

    private static async Task TrySynchronizeTestSubjectVisualStateAsync(
        Creature creature,
        CombatCreatureSnapshot snapshot,
        string restoreContext,
        bool refreshIntentDisplays)
    {
        try
        {
            var creatureNode = NCombatRoom.Instance?.GetCreatureNode(creature);
            if (creatureNode == null || !creatureNode.HasSpineAnimation || creatureNode.Visuals.SpineBody == null)
            {
                MainFile.Logger.Warn(
                    $"Skipped Test Subject visual sync during singleplayer combat snapshot restore for {restoreContext} " +
                    $"because the creature node or spine visuals were unavailable. Slot={snapshot.SlotName ?? "unknown"}");
                return;
            }

            var respawns = TryReadSpecialFieldInt(snapshot.SpecialFields, "_respawns", out var restoredRespawns)
                ? Math.Max(0, restoredRespawns)
                : 0;
            var animationId = ResolveTestSubjectAnimationId(snapshot, respawns);
            var spineBody = creatureNode.Visuals.SpineBody;
            if (!spineBody.HasAnimation(animationId))
            {
                MainFile.Logger.Warn(
                    $"Skipped Test Subject visual sync during singleplayer combat snapshot restore for {restoreContext} " +
                    $"because animation {animationId} was unavailable. Slot={snapshot.SlotName ?? "unknown"} Respawns={respawns}");
                return;
            }

            creatureNode.SetDefaultScaleTo(1f + respawns * 0.1f, 0f);
            creatureNode.SpineAnimation.SetAnimation(
                animationId,
                !string.Equals(animationId, TestSubjectDieAnimationId, StringComparison.Ordinal),
                0);
            if (refreshIntentDisplays)
                await creatureNode.RefreshIntents();

            MainFile.Logger.Info(
                $"Synchronized Test Subject visual state during singleplayer combat snapshot restore for {restoreContext}. " +
                $"Slot={snapshot.SlotName ?? "unknown"} Respawns={respawns} Move={snapshot.CurrentMoveStateId ?? "unknown"} Anim={animationId}");
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn(
                $"Failed to synchronize Test Subject visual state during singleplayer combat snapshot restore for {restoreContext}. " +
                $"Slot={snapshot.SlotName ?? "unknown"} Move={snapshot.CurrentMoveStateId ?? "unknown"}: {e.Message}");
        }
    }

    private static string ResolveTestSubjectAnimationId(CombatCreatureSnapshot snapshot, int respawns)
    {
        if (string.Equals(snapshot.CurrentMoveStateId, TestSubjectRespawnMoveId, StringComparison.Ordinal)
            || snapshot.CurrentHp <= 0)
        {
            return respawns switch
            {
                <= 0 => TestSubjectKnockedOutLoop1AnimationId,
                1 => TestSubjectKnockedOutLoop2AnimationId,
                _ => TestSubjectDieAnimationId
            };
        }

        return respawns switch
        {
            <= 0 => TestSubjectIdleLoop1AnimationId,
            1 => TestSubjectIdleLoop2AnimationId,
            _ => TestSubjectIdleLoop3AnimationId
        };
    }

    private static void TrySynchronizeKaiserCrabRocketVisualState(CombatCreatureSnapshot snapshot, string restoreContext)
    {
        try
        {
            var background = NCombatRoom.Instance?.Background?.GetNodeOrNull<NKaiserCrabBossBackground>("%KaiserCrab");
            if (background == null)
            {
                MainFile.Logger.Warn(
                    $"Skipped Kaiser Crab rocket visual sync during singleplayer combat snapshot restore for {restoreContext} " +
                    $"because the boss background was unavailable. Move={snapshot.CurrentMoveStateId ?? "unknown"}");
                return;
            }

            var visuals = background.GetNodeOrNull<Node2D>("%Visuals");
            if (visuals == null)
            {
                MainFile.Logger.Warn(
                    $"Skipped Kaiser Crab rocket visual sync during singleplayer combat snapshot restore for {restoreContext} " +
                    $"because the boss visuals node was unavailable. Move={snapshot.CurrentMoveStateId ?? "unknown"}");
                return;
            }

            var animationState = new MegaSprite(visuals).GetAnimationState();
            switch (snapshot.CurrentMoveStateId)
            {
                case RocketLaserMoveId:
                    SetKaiserCrabRightArmState(background, 1);
                    animationState.SetAnimation("right/charged_loop", true, KaiserCrabRightArmTrack);
                    break;

                case RocketRechargeMoveId:
                    SetKaiserCrabRightArmState(background, 2);
                    animationState.SetAnimation("right/rest_loop", true, KaiserCrabRightArmTrack);
                    break;

                default:
                    SetKaiserCrabRightArmState(background, 0);
                    animationState.SetAnimation("right/idle_loop", true, KaiserCrabRightArmTrack);
                    break;
            }

            MainFile.Logger.Info(
                $"Synchronized Kaiser Crab rocket visual state during singleplayer combat snapshot restore for {restoreContext}. " +
                $"Move={snapshot.CurrentMoveStateId ?? "unknown"}");
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn(
                $"Failed to synchronize Kaiser Crab rocket visual state during singleplayer combat snapshot restore for {restoreContext}. " +
                $"Move={snapshot.CurrentMoveStateId ?? "unknown"}: {e.Message}");
        }
    }

    private static void SetKaiserCrabRightArmState(NKaiserCrabBossBackground background, int rawState)
    {
        if (KaiserCrabRightArmStateField == null)
            return;

        try
        {
            var enumValue = Enum.ToObject(KaiserCrabRightArmStateField.FieldType, rawState);
            KaiserCrabRightArmStateField.SetValue(background, enumValue);
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"Failed to set Kaiser Crab right arm state to {rawState}: {e.Message}");
        }
    }

    private static void RepositionToughEggNode(
        Creature creature,
        ToughEgg toughEgg,
        CombatCreatureSnapshot snapshot,
        NCreature? creatureNode)
    {
        if (creatureNode == null)
            return;

        if (toughEgg.IsHatched)
        {
            if (snapshot.NodeGlobalPosition != null)
                creatureNode.GlobalPosition = snapshot.NodeGlobalPosition.ToVector2();

            return;
        }

        if (TryGetEncounterSlotPosition(creature.SlotName, out var slotPosition))
            creatureNode.GlobalPosition = slotPosition;
    }

    private static bool TryGetEncounterSlotPosition(string? slotName, out Vector2 slotPosition)
    {
        slotPosition = Vector2.Zero;

        var encounterSlots = NCombatRoomEncounterSlotsProperty?.GetValue(NCombatRoom.Instance) as Control;
        if (encounterSlots == null || string.IsNullOrWhiteSpace(slotName))
            return false;

        var slotMarker = encounterSlots.GetNodeOrNull<Marker2D>(slotName);
        if (slotMarker == null)
            return false;

        slotPosition = slotMarker.GlobalPosition;
        return true;
    }

    private static async Task EnsureTemporaryStunnedMoveStateAsync(Creature? creature, CombatCreatureSnapshot snapshot, string restoreContext)
    {
        if (creature == null || !snapshot.IsTemporaryStunned)
            return;

        var moveStateMachine = creature.Monster?.MoveStateMachine;
        if (moveStateMachine?.States.ContainsKey(snapshot.CurrentMoveStateId ?? string.Empty) == true)
            return;

        try
        {
            if (creature.Monster is BowlbugRock bowlbugRock)
            {
                await CreatureCmd.Stun(
                    creature,
                    async _ =>
                    {
                        bowlbugRock.IsOffBalance = false;
                        await CreatureCmd.TriggerAnim(creature, "Unstun", 0.6f);
                    },
                    snapshot.CurrentMoveFollowUpStateId ?? string.Empty);
            }
            else
            {
                await CreatureCmd.Stun(creature, snapshot.CurrentMoveFollowUpStateId ?? string.Empty);
            }

            MainFile.Logger.Info(
                $"Recreated temporary stunned move state for {restoreContext}. " +
                $"Monster={snapshot.MonsterId}, Slot={snapshot.SlotName}, FollowUp={snapshot.CurrentMoveFollowUpStateId ?? "null"}");
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn(
                $"Failed to recreate temporary stunned move state for {restoreContext}. " +
                $"Monster={snapshot.MonsterId}, Slot={snapshot.SlotName}, FollowUp={snapshot.CurrentMoveFollowUpStateId ?? "null"}: {e.Message}");
        }
    }

    private static void ApplyFollowUpStateSnapshot(
        MonsterMoveStateMachine moveStateMachine,
        MoveState moveState,
        CombatCreatureSnapshot snapshot,
        Creature? creature)
    {
        if (string.IsNullOrWhiteSpace(snapshot.CurrentMoveFollowUpStateId))
            return;

        if (moveStateMachine.States.TryGetValue(snapshot.CurrentMoveFollowUpStateId, out var followUpState))
        {
            moveState.FollowUpState = followUpState;
        }
        else
        {
            MainFile.Logger.Warn(
                $"Could not resolve follow-up move state {snapshot.CurrentMoveFollowUpStateId} while restoring " +
                $"{snapshot.MonsterId ?? creature?.Monster?.Id.Entry ?? "unknown"} at slot {snapshot.SlotName ?? "unknown"}.");
        }
    }

    private static void RestorePowerExtras(
        RunState runState,
        CombatState combatState,
        IReadOnlyList<CombatCreatureSnapshot> snapshots,
        string restoreContext,
        SnapshotResolutionContext resolutionContext)
    {
        var usedDeckCardsByPlayer = BuildUsedDeckCardsLookup(combatState);

        foreach (var snapshot in snapshots)
        {
            var creature = ResolveCreature(combatState, snapshot, resolutionContext);
            if (creature == null)
                continue;

            foreach (var powerSnapshot in snapshot.Powers)
            {
                var restoredPower = creature.Powers.LastOrDefault(power => power.Id == powerSnapshot.Id);
                if (restoredPower == null)
                    continue;

                RestorePowerTarget(restoredPower, powerSnapshot, combatState, restoreContext);
                RestoreSwipePowerState(restoredPower, powerSnapshot, runState, combatState, usedDeckCardsByPlayer, restoreContext);

                foreach (var fieldSnapshot in powerSnapshot.SpecialFields)
                {
                    TryRestoreField(restoredPower, fieldSnapshot, $"power {powerSnapshot.Id}");
                }
            }
        }
    }

    private static void RestorePowerTarget(PowerModel restoredPower, CombatPowerSnapshot powerSnapshot, CombatState combatState, string restoreContext)
    {
        if (!powerSnapshot.TargetPlayerId.HasValue)
            return;

        var targetPlayer = combatState.RunState?.GetPlayer(powerSnapshot.TargetPlayerId.Value);
        if (targetPlayer?.Creature == null)
        {
            MainFile.Logger.Warn(
                $"Skipped restoring target for power {powerSnapshot.Id} during singleplayer combat snapshot restore for {restoreContext} " +
                $"because player {powerSnapshot.TargetPlayerId.Value} was not found.");
            return;
        }

        switch (restoredPower)
        {
            case ThieveryPower thieveryPower:
                thieveryPower.Target = targetPlayer.Creature;
                break;
            case SwipePower swipePower:
                swipePower.Target = targetPlayer.Creature;
                break;
            case SandpitPower sandpitPower:
                sandpitPower.Target = targetPlayer.Creature;
                break;
        }
    }

    private static Dictionary<ulong, HashSet<CardModel>> BuildUsedDeckCardsLookup(CombatState combatState)
    {
        var lookup = new Dictionary<ulong, HashSet<CardModel>>();
        foreach (var player in combatState.Players)
        {
            var usedDeckCards = player.PlayerCombatState?.AllPiles
                .SelectMany(pile => pile.Cards)
                .Select(card => card.DeckVersion)
                .OfType<CardModel>()
                .ToHashSet()
                ?? [];
            lookup[player.NetId] = usedDeckCards;
        }

        return lookup;
    }

    private static HashSet<CardModel> GetOrCreateUsedDeckCards(
        IDictionary<ulong, HashSet<CardModel>> usedDeckCardsByPlayer,
        Player player)
    {
        if (usedDeckCardsByPlayer.TryGetValue(player.NetId, out var usedDeckCards))
            return usedDeckCards;

        usedDeckCards = [];
        usedDeckCardsByPlayer[player.NetId] = usedDeckCards;
        return usedDeckCards;
    }

    private static void RestoreSwipePowerState(
        PowerModel restoredPower,
        CombatPowerSnapshot powerSnapshot,
        RunState runState,
        CombatState combatState,
        IDictionary<ulong, HashSet<CardModel>> usedDeckCardsByPlayer,
        string restoreContext)
    {
        if (restoredPower is not SwipePower swipePower)
            return;

        if (powerSnapshot.SwipeStolenCard == null)
        {
            swipePower.StolenCard = null;
            return;
        }

        var targetPlayer = powerSnapshot.TargetPlayerId.HasValue
            ? combatState.RunState?.GetPlayer(powerSnapshot.TargetPlayerId.Value)
            : swipePower.Target?.Player;
        if (targetPlayer == null)
        {
            MainFile.Logger.Warn(
                $"Skipped restoring stolen card for swipe power during singleplayer combat snapshot restore for {restoreContext} " +
                $"because player {powerSnapshot.TargetPlayerId?.ToString() ?? "unknown"} was not found.");
            swipePower.StolenCard = null;
            return;
        }

        var usedDeckCards = GetOrCreateUsedDeckCards(usedDeckCardsByPlayer, targetPlayer);
        swipePower.StolenCard = RestoreDetachedCardFromSnapshot(runState, targetPlayer, powerSnapshot.SwipeStolenCard, usedDeckCards, restoreContext);
    }

    private static async Task SynchronizePostRestoreSpecialPowerStateAsync(
        CombatState combatState,
        IReadOnlyList<CombatCreatureSnapshot> snapshots,
        string restoreContext,
        SnapshotResolutionContext resolutionContext)
    {
        foreach (var snapshot in snapshots)
        {
            var creature = ResolveCreature(combatState, snapshot, resolutionContext);
            if (creature == null)
                continue;

            foreach (var powerSnapshot in snapshot.Powers)
            {
                var restoredPower = creature.Powers.LastOrDefault(power => power.Id == powerSnapshot.Id);
                if (restoredPower == null)
                    continue;

                await TrySynchronizeSpecialPowerStateAsync(restoredPower, powerSnapshot, restoreContext);
            }

            SynchronizeThievingHopperStolenCardDisplay(creature, restoreContext);
        }
    }

    private static async Task TrySynchronizeSpecialPowerStateAsync(PowerModel restoredPower, CombatPowerSnapshot powerSnapshot, string restoreContext)
    {
        if (restoredPower is SurroundedPower surroundedPower)
        {
            await SynchronizeSurroundedPowerStateAsync(surroundedPower, restoreContext);
            return;
        }

        if (restoredPower is not SandpitPower sandpitPower)
            return;

        if (sandpitPower.Target?.Player == null)
        {
            MainFile.Logger.Warn(
                $"Skipped synchronizing sandpit power during singleplayer combat snapshot restore for {restoreContext} " +
                $"because its target was missing. Amount={sandpitPower.Amount}");
            return;
        }

        if (SandpitPowerUpdateCreaturePositionsMethod == null)
        {
            MainFile.Logger.Warn(
                $"Skipped synchronizing sandpit power during singleplayer combat snapshot restore for {restoreContext} " +
                "because UpdateCreaturePositions could not be reflected.");
            return;
        }

        try
        {
            if (SandpitPowerUpdateCreaturePositionsMethod.Invoke(sandpitPower, null) is Task updateTask)
                await updateTask;

            MainFile.Logger.Info(
                $"Synchronized sandpit power during singleplayer combat snapshot restore for {restoreContext}. " +
                $"Amount={sandpitPower.Amount} TargetPlayer={sandpitPower.Target.Player.NetId}");
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn(
                $"Failed to synchronize sandpit power during singleplayer combat snapshot restore for {restoreContext}. " +
                $"Amount={sandpitPower.Amount} TargetPlayer={sandpitPower.Target.Player.NetId}: {e.Message}");
        }
    }

    private static async Task SynchronizeSurroundedPowerStateAsync(SurroundedPower surroundedPower, string restoreContext)
    {
        if (SurroundedPowerFaceDirectionMethod == null)
        {
            MainFile.Logger.Warn(
                $"Skipped synchronizing surrounded power during singleplayer combat snapshot restore for {restoreContext} " +
                "because FaceDirection could not be reflected.");
            return;
        }

        try
        {
            if (SurroundedPowerFaceDirectionMethod.Invoke(surroundedPower, [surroundedPower.Facing]) is Task faceTask)
                await faceTask;

            await RefreshEnemyIntentDisplaysAsync(surroundedPower.Owner?.CombatState);
            var ownerPlayerId = surroundedPower.Owner?.Player is Player ownerPlayer
                ? ownerPlayer.NetId.ToString()
                : "unknown";

            MainFile.Logger.Info(
                $"Synchronized surrounded power during singleplayer combat snapshot restore for {restoreContext}. " +
                $"Facing={surroundedPower.Facing} OwnerPlayer={ownerPlayerId}");
        }
        catch (Exception e)
        {
            var ownerPlayerId = surroundedPower.Owner?.Player is Player ownerPlayer
                ? ownerPlayer.NetId.ToString()
                : "unknown";
            MainFile.Logger.Warn(
                $"Failed to synchronize surrounded power during singleplayer combat snapshot restore for {restoreContext}. " +
                $"Facing={surroundedPower.Facing} OwnerPlayer={ownerPlayerId}: {e.Message}");
        }
    }

    private static async Task RefreshEnemyIntentDisplaysAsync(ICombatState? combatState)
    {
        var combatRoom = NCombatRoom.Instance;
        var mutableCombatState = CombatStateCompatibilityService.GetCombatState(combatState);
        if (mutableCombatState == null || combatRoom == null)
            return;

        foreach (var enemy in mutableCombatState.Creatures.Where(creature => creature.Player == null))
        {
            var creatureNode = combatRoom.GetCreatureNode(enemy);
            if (creatureNode != null)
                await creatureNode.RefreshIntents();
        }
    }

    private static async Task TryRefreshEnemyIntentDisplaysAsync(ICombatState? combatState, string restoreContext)
    {
        try
        {
            await RefreshEnemyIntentDisplaysAsync(combatState);
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"Failed to refresh singleplayer enemy intents for {restoreContext}: {e.Message}");
        }
    }

    private static void RestoreHiddenLiveAllyVisuals(CombatState combatState, string restoreContext)
    {
        var combatRoom = NCombatRoom.Instance;
        if (combatRoom == null)
            return;

        var restoredCreatures = new List<string>();

        foreach (var creature in combatState.Allies.Where(creature => !creature.IsDead))
        {
            var creatureNode = combatRoom.GetCreatureNode(creature);
            var visuals = creatureNode?.Visuals;
            if (creatureNode == null || visuals == null)
                continue;

            var shouldRestoreVisibility = !creatureNode.Visible
                || !visuals.Visible
                || visuals.Modulate.A < 0.99f;
            if (!shouldRestoreVisibility)
                continue;

            creatureNode.Visible = true;
            visuals.Visible = true;
            visuals.Modulate = Colors.White;
            creatureNode.StartReviveAnim();

            restoredCreatures.Add(creature.IsPlayer
                ? $"player:{creature.Player?.NetId}"
                : creature.Monster?.Id.Entry ?? "ally");
        }

        if (restoredCreatures.Count == 0)
            return;

        MainFile.Logger.Info(
            $"Restored hidden live ally visuals during singleplayer combat snapshot restore for {restoreContext}: " +
            $"{string.Join(", ", restoredCreatures)}");
    }

    private static void SynchronizeThievingHopperStolenCardDisplay(Creature creature, string restoreContext)
    {
        if (creature.Monster is not ThievingHopper)
            return;

        var creatureNode = NCombatRoom.Instance?.GetCreatureNode(creature);
        var stolenCardMarker = creatureNode?.GetSpecialNode<Marker2D>("%StolenCardPos");
        if (stolenCardMarker == null)
            return;

        foreach (var child in stolenCardMarker.GetChildren())
            child.QueueFree();

        var swipePower = creature.Powers.OfType<SwipePower>().LastOrDefault();
        var stolenCard = swipePower?.StolenCard;
        if (stolenCard == null || !LocalContext.IsMine(stolenCard))
            return;

        var stolenCardNode = NCard.Create(stolenCard, ModelVisibility.Visible);
        if (stolenCardNode == null)
            return;

        stolenCardMarker.AddChild(stolenCardNode);
        stolenCardNode.Position += stolenCardNode.Size * 0.5f;
        stolenCardNode.UpdateVisuals(PileType.Deck, CardPreviewMode.Normal);

        MainFile.Logger.Info(
            $"Synchronized thieving hopper stolen-card display during singleplayer combat snapshot restore for {restoreContext}. " +
            $"Monster={creature.Monster?.Id.Entry ?? "THIEVING_HOPPER"} Card={stolenCard.Id.Entry}");
    }

    private static void RestorePlayerPiles(RunState runState, Player player, IReadOnlyList<CombatPileSnapshot> pileSnapshots)
    {
        var combat = player.PlayerCombatState!;
        var usedDeckCards = new HashSet<CardModel>();
        foreach (var existingCard in combat.AllPiles.SelectMany(pile => pile.Cards).ToList())
            RemoveCardFromRestoreState(existingCard);

        foreach (var pileSnapshot in pileSnapshots)
        {
            var targetPile = CardPile.Get(pileSnapshot.PileType, player);
            if (targetPile == null)
                continue;

            foreach (var cardSnapshot in pileSnapshot.Cards)
            {
                var card = runState.LoadCard(cardSnapshot.Card, player);
                RegisterCardInCombatState(card);
                card.DeckVersion = ResolveDeckVersion(runState, player, cardSnapshot, usedDeckCards);
                ApplyCardSnapshotState(card, cardSnapshot);

                targetPile.AddInternal(card, -1, false);
            }
        }
    }

    private static CardModel? ResolveDeckVersion(
        RunState runState,
        Player player,
        CombatCardSnapshot cardSnapshot,
        ISet<CardModel> usedDeckCards)
    {
        if (!cardSnapshot.HadDeckVersion && cardSnapshot.DeckVersionCard == null)
            return null;

        var desiredDeckCard = cardSnapshot.DeckVersionCard ?? cardSnapshot.Card;
        var matchingDeckCard = player.Deck.Cards.FirstOrDefault(deckCard =>
            !usedDeckCards.Contains(deckCard)
            && AreSerializableCardsEquivalent(deckCard.ToSerializable(), desiredDeckCard));
        if (matchingDeckCard != null)
        {
            usedDeckCards.Add(matchingDeckCard);
            return matchingDeckCard;
        }

        try
        {
            var recreatedDeckCard = runState.LoadCard(desiredDeckCard, player);
            player.Deck.AddInternal(recreatedDeckCard, -1, false);
            usedDeckCards.Add(recreatedDeckCard);
            return recreatedDeckCard;
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn(
                $"Failed to recreate missing deck version for singleplayer combat card snapshot restore. " +
                $"Player={player.NetId}, Card={desiredDeckCard.Id}: {e.Message}");
            return null;
        }
    }

    private static CardModel? RestoreDetachedCardFromSnapshot(
        RunState runState,
        Player player,
        CombatCardSnapshot cardSnapshot,
        ISet<CardModel> usedDeckCards,
        string restoreContext)
    {
        try
        {
            var card = runState.LoadCard(cardSnapshot.Card, player);
            RegisterCardInCombatState(card);
            card.DeckVersion = ResolveRemovedDeckVersion(player, cardSnapshot, usedDeckCards);
            ApplyCardSnapshotState(card, cardSnapshot);
            card.RemoveFromState();
            return card;
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn(
                $"Failed to restore detached combat card for singleplayer combat snapshot restore for {restoreContext}. " +
                $"Player={player.NetId}, Card={cardSnapshot.Card.Id}: {e.Message}");
            return null;
        }
    }

    private static CardModel? ResolveRemovedDeckVersion(
        Player player,
        CombatCardSnapshot cardSnapshot,
        ISet<CardModel> usedDeckCards)
    {
        if (!cardSnapshot.HadDeckVersion && cardSnapshot.DeckVersionCard == null)
            return null;

        var desiredDeckCard = cardSnapshot.DeckVersionCard ?? cardSnapshot.Card;
        var matchingDeckCard = player.Deck.Cards.FirstOrDefault(deckCard =>
            !usedDeckCards.Contains(deckCard)
            && AreSerializableCardsEquivalent(deckCard.ToSerializable(), desiredDeckCard));

        if (matchingDeckCard != null)
        {
            usedDeckCards.Add(matchingDeckCard);
            return matchingDeckCard;
        }

        var deckVersion = CreateDetachedDeckVersionCard(desiredDeckCard);
        usedDeckCards.Add(deckVersion);
        return deckVersion;
    }

    private static bool AreSerializableCardsEquivalent(SerializableCard left, SerializableCard right)
    {
        return JsonSerializer.Serialize(left, JsonOptions) == JsonSerializer.Serialize(right, JsonOptions);
    }

    private static CardModel CreateDetachedDeckVersionCard(SerializableCard serializableCard)
    {
        return CardModel.FromSerializable(serializableCard);
    }

    private static void ApplyCardSnapshotState(CardModel card, CombatCardSnapshot cardSnapshot)
    {
        if (cardSnapshot.Affliction != null)
        {
            var affliction = ModelDb.GetById<AfflictionModel>(cardSnapshot.Affliction).ToMutable();
            card.AfflictInternal(affliction, cardSnapshot.AfflictionCount);
        }

        if (cardSnapshot.Keywords == null)
            return;

        foreach (var keyword in cardSnapshot.Keywords)
        {
            if (!card.Keywords.Contains(keyword))
                card.AddKeyword(keyword);
        }
    }

    private static void RestorePlayerOrbs(Player player, PlayerCombatState combat, IReadOnlyList<CombatOrbSnapshot> orbSnapshots)
    {
        var orbQueue = combat.OrbQueue;
        var capacity = Math.Max(orbQueue.Capacity, orbSnapshots.Count);
        orbQueue.Clear();
        orbQueue.AddCapacity(capacity);

        for (var index = 0; index < orbSnapshots.Count; index++)
        {
            var orbSnapshot = orbSnapshots[index];
            var orb = ModelDb.GetById<OrbModel>(orbSnapshot.Id).ToMutable();
            orb.Owner = player;
            RestoreOrbSnapshotState(orb, orbSnapshot);
            orbQueue.Insert(index, orb);
        }
    }

    private static void SynchronizePlayerOrbManagersForSingleplayerRestore(CombatState combatState, string restoreContext)
    {
        foreach (var player in combatState.Players)
            TrySynchronizePlayerOrbManagerForSingleplayerRestore(player, restoreContext);
    }

    private static void TrySynchronizePlayerOrbManagerForSingleplayerRestore(Player player, string restoreContext)
    {
        var orbQueue = player.PlayerCombatState?.OrbQueue;
        var orbManager = NCombatRoom.Instance?.GetCreatureNode(player.Creature)?.OrbManager;
        if (orbQueue == null || orbManager == null)
            return;

        try
        {
            RebuildOrbManagerVisualsFromQueue(orbManager, orbQueue);

            if (orbQueue.Capacity > 0 || orbQueue.Orbs.Count > 0)
            {
                MainFile.Logger.Info(
                    $"Rebuilt singleplayer orb visuals after restore for {restoreContext}. " +
                    $"Player={player.NetId} Orbs={orbQueue.Orbs.Count}/{orbQueue.Capacity}");
            }
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn(
                $"Failed to rebuild singleplayer orb visuals after restore for {restoreContext}. " +
                $"Player={player.NetId}: {e.Message}");
        }
    }

    private static void RebuildOrbManagerVisualsFromQueue(NOrbManager orbManager, OrbQueue orbQueue)
    {
        if (NOrbManagerOrbContainerField?.GetValue(orbManager) is not Control orbContainer
            || NOrbManagerOrbsField?.GetValue(orbManager) is not IList orbNodes)
        {
            return;
        }

        if (NOrbManagerCurrentTweenField?.GetValue(orbManager) is Tween currentTween)
            currentTween.Kill();

        foreach (var orbNode in orbContainer.GetChildren().OfType<NOrb>().ToList())
            ForceRemoveOrbNodeImmediately(orbNode);

        orbNodes.Clear();

        foreach (var orb in orbQueue.Orbs)
            AddOrbNodeToManager(orbManager, orbContainer, orbNodes, NOrb.Create(orbManager.IsLocal, orb));

        for (var index = orbQueue.Orbs.Count; index < orbQueue.Capacity; index++)
            AddOrbNodeToManager(orbManager, orbContainer, orbNodes, NOrb.Create(orbManager.IsLocal));

        NOrbManagerTweenLayoutMethod?.Invoke(orbManager, []);
        NOrbManagerUpdateControllerNavigationMethod?.Invoke(orbManager, []);
        orbManager.UpdateVisuals(OrbEvokeType.None);
    }

    private static void AddOrbNodeToManager(NOrbManager orbManager, Control orbContainer, IList orbNodes, NOrb orbNode)
    {
        orbContainer.AddChild(orbNode);
        orbNodes.Add(orbNode);
        orbNode.Position = Vector2.Zero;
    }

    private static void ForceRemoveOrbNodeImmediately(NOrb orbNode)
    {
        try
        {
            orbNode.Visible = false;
            orbNode.GetParent()?.RemoveChild(orbNode);
            orbNode.QueueFree();
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }

    private static void RestoreOrbSnapshotState(OrbModel orb, CombatOrbSnapshot orbSnapshot)
    {
        switch (orb)
        {
            case DarkOrb:
                DarkOrbEvokeValueField?.SetValue(orb, (decimal)orbSnapshot.Evoke);
                break;
            case GlassOrb:
                GlassOrbPassiveValueField?.SetValue(orb, (decimal)orbSnapshot.Passive);
                break;
        }
    }

    private static void RestoreCombatManagerState(CombatManagerSnapshot? snapshot, string restoreContext)
    {
        if (snapshot == null)
            return;

        var combatManager = CombatManager.Instance;
        if (combatManager == null)
            return;

        try
        {
            if (snapshot.PlayerActionsDisabledField != null)
                TryRestoreField(combatManager, snapshot.PlayerActionsDisabledField, "combat manager player-actions-disabled");

            if (snapshot.PlayersReadyToEndTurnField != null)
                TryRestoreField(combatManager, snapshot.PlayersReadyToEndTurnField, "combat manager players-ready-to-end-turn");

            if (snapshot.PlayersReadyToBeginEnemyTurnField != null)
                TryRestoreField(combatManager, snapshot.PlayersReadyToBeginEnemyTurnField, "combat manager players-ready-to-begin-enemy-turn");

            CombatStateCompatibilityService.RestorePlayPhaseIfNeeded(combatManager, snapshot.IsPlayPhase);
            TryRestoreBooleanMember(
                combatManager,
                CombatManagerIsEnemyTurnStartedProperty,
                CombatManagerIsEnemyTurnStartedField,
                snapshot.IsEnemyTurnStarted,
                "combat manager IsEnemyTurnStarted");
            TryRestoreBooleanMember(
                combatManager,
                CombatManagerEndingPlayerTurnPhaseOneProperty,
                CombatManagerEndingPlayerTurnPhaseOneField,
                snapshot.EndingPlayerTurnPhaseOne,
                "combat manager EndingPlayerTurnPhaseOne");
            TryRestoreBooleanMember(
                combatManager,
                CombatManagerEndingPlayerTurnPhaseTwoProperty,
                CombatManagerEndingPlayerTurnPhaseTwoField,
                snapshot.EndingPlayerTurnPhaseTwo,
                "combat manager EndingPlayerTurnPhaseTwo");

            MainFile.Logger.Info(
                $"Restored singleplayer combat manager runtime state for {restoreContext}. " +
                $"playerActionsDisabled={CombatManager.Instance.PlayerActionsDisabled} " +
                $"isPlayPhase={CombatStateCompatibilityService.IsPlayPhase(CombatStateCompatibilityService.GetCurrentCombatState())} " +
                $"phase={CombatStateCompatibilityService.DescribePlayerTurnPhases(CombatStateCompatibilityService.GetCurrentCombatState())} " +
                $"isEnemyTurnStarted={CombatManager.Instance.IsEnemyTurnStarted} " +
                $"endingPhaseOne={CombatManager.Instance.EndingPlayerTurnPhaseOne} " +
                $"endingPhaseTwo={CombatManager.Instance.EndingPlayerTurnPhaseTwo} " +
                $"handCanPlay={GetHandCanPlayCardsState()?.ToString() ?? "unknown"} " +
                $"handActionsAllowed={GetHandActionsAllowedState()?.ToString() ?? "unknown"}");
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"Failed to restore singleplayer combat manager runtime state for {restoreContext}: {e.Message}");
        }
    }

    private static void ReinitializeCombatReplayWriter(string restoreContext)
    {
        var runManager = RunManager.Instance;
        var replayWriter = runManager?.CombatReplayWriter;
        if (runManager == null || replayWriter == null || CombatReplayWriterRecordInitialStateMethod == null)
            return;

        try
        {
            var save = runManager.ToSave(null);
            CombatReplayWriterRecordInitialStateMethod.Invoke(replayWriter, [save]);
            MainFile.Logger.Info($"Reinitialized singleplayer combat replay writer for {restoreContext}.");
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"Failed to reinitialize singleplayer combat replay writer for {restoreContext}: {e.Message}");
        }
    }

    private static async Task ReconcileCombatRoomCreatureNodes(
        CombatState combatState,
        IReadOnlyList<CombatCreatureSnapshot> snapshots,
        string restoreContext,
        SnapshotResolutionContext resolutionContext)
    {
        var combatRoomNode = NCombatRoom.Instance;
        if (combatRoomNode == null)
            return;

        var activeCreatures = combatState.Creatures.ToHashSet();
        var orphanNodes = combatRoomNode.CreatureNodes
            .Where(node => node.Entity == null || !activeCreatures.Contains(node.Entity))
            .ToList();
        foreach (var node in orphanNodes)
            RemoveCreatureNodeFromCombatRoom(combatRoomNode, node);

        var orphanRemovingNodes = combatRoomNode.RemovingCreatureNodes
            .Where(node => node.Entity == null || !activeCreatures.Contains(node.Entity))
            .ToList();
        foreach (var node in orphanRemovingNodes)
            RemoveCreatureNodeFromCombatRoom(combatRoomNode, node);

        var resolvedToughEggSnapshots = resolutionContext.GetResolvedSnapshots("TOUGH_EGG");
        var toughEggShadowNodes = FindToughEggShadowNodes(combatRoomNode, resolvedToughEggSnapshots);
        foreach (var node in toughEggShadowNodes)
            RemoveCreatureNodeFromCombatRoom(combatRoomNode, node);

        await SynchronizeToughEggNodePositionsAsync(combatRoomNode, resolvedToughEggSnapshots, restoreContext);

        var removedNodes = orphanNodes
            .Concat(orphanRemovingNodes)
            .Concat(toughEggShadowNodes)
            .Distinct()
            .ToList();
        if (removedNodes.Count == 0)
            return;

        MainFile.Logger.Info(
            $"Removed {removedNodes.Count} stale combat room creature node(s) after singleplayer restore for {restoreContext}: " +
            $"{string.Join(", ", removedNodes.Select(DescribeCreatureNodeForLog))}");
    }

    private static SnapshotResolutionContext BuildSnapshotResolutionContext(
        CombatState combatState,
        IReadOnlyList<CombatCreatureSnapshot> snapshots)
    {
        var resolutionContext = new SnapshotResolutionContext();
        foreach (var resolvedSnapshot in ResolveUniqueMonsterSnapshots(combatState, snapshots, "TOUGH_EGG"))
            resolutionContext.Register(resolvedSnapshot.Snapshot, resolvedSnapshot.Creature);

        return resolutionContext;
    }

    private static List<ResolvedCreatureSnapshot> ResolveUniqueMonsterSnapshots(
        CombatState combatState,
        IReadOnlyList<CombatCreatureSnapshot> snapshots,
        string monsterId)
    {
        var resolvedSnapshots = new List<ResolvedCreatureSnapshot>();
        var usedCreatures = new HashSet<Creature>();

        foreach (var snapshot in snapshots.Where(snapshot => string.Equals(snapshot.MonsterId, monsterId, StringComparison.Ordinal)))
        {
            var creature = ResolveCreature(combatState, snapshot, usedCreatures);
            if (creature == null)
                continue;

            usedCreatures.Add(creature);
            resolvedSnapshots.Add(new ResolvedCreatureSnapshot(snapshot, creature));
        }

        return resolvedSnapshots;
    }

    private static List<NCreature> FindToughEggShadowNodes(
        NCombatRoom combatRoomNode,
        IReadOnlyList<ResolvedCreatureSnapshot> resolvedSnapshots)
    {
        var expectedCreaturesBySlot = resolvedSnapshots
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Snapshot.SlotName))
            .GroupBy(entry => entry.Snapshot.SlotName!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Creature, StringComparer.Ordinal);

        if (expectedCreaturesBySlot.Count == 0)
            return [];

        return combatRoomNode.CreatureNodes
            .Concat(combatRoomNode.RemovingCreatureNodes)
            .Where(node => node.Entity?.Monster is ToughEgg)
            .Where(node =>
            {
                var slotName = node.Entity?.SlotName;
                return !string.IsNullOrWhiteSpace(slotName)
                    && expectedCreaturesBySlot.TryGetValue(slotName!, out var expectedCreature)
                    && node.Entity != expectedCreature;
            })
            .Distinct()
            .ToList();
    }

    private static async Task SynchronizeToughEggNodePositionsAsync(
        NCombatRoom combatRoomNode,
        IReadOnlyList<ResolvedCreatureSnapshot> resolvedSnapshots,
        string restoreContext)
    {
        foreach (var resolvedSnapshot in resolvedSnapshots)
        {
            if (resolvedSnapshot.Creature.Monster is not ToughEgg toughEgg)
                continue;

            var ensuredNode = EnsureCreatureNodeExistsForRestore(
                combatRoomNode,
                resolvedSnapshot.Creature,
                resolvedSnapshot.Snapshot);

            if (ensuredNode.Node == null)
                continue;

            if (ensuredNode.WasRecreated)
            {
                await TrySynchronizeSpecialMonsterStateAsync(
                    resolvedSnapshot.Creature,
                    resolvedSnapshot.Snapshot,
                    $"{restoreContext}/tough-egg-node-resync",
                    refreshIntentDisplays: true);
                continue;
            }

            RepositionToughEggNode(resolvedSnapshot.Creature, toughEgg, resolvedSnapshot.Snapshot, ensuredNode.Node);
        }
    }

    private static EnsuredCreatureNodeResult EnsureCreatureNodeExistsForRestore(
        NCombatRoom combatRoomNode,
        Creature creature,
        CombatCreatureSnapshot snapshot)
    {
        var creatureNode = combatRoomNode.GetCreatureNode(creature);
        if (creatureNode != null)
            return new EnsuredCreatureNodeResult(creatureNode, false);

        var staleRemovingNodes = combatRoomNode.RemovingCreatureNodes
            .Where(node => node.Entity == creature
                || (node.Entity?.Monster is ToughEgg
                    && !string.IsNullOrWhiteSpace(snapshot.SlotName)
                    && string.Equals(node.Entity?.SlotName, snapshot.SlotName, StringComparison.Ordinal)))
            .Distinct()
            .ToList();
        foreach (var staleNode in staleRemovingNodes)
            RemoveCreatureNodeFromCombatRoom(combatRoomNode, staleNode);

        try
        {
            combatRoomNode.AddCreature(creature);
            return new EnsuredCreatureNodeResult(combatRoomNode.GetCreatureNode(creature), true);
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn(
                $"Failed to recreate missing combat room creature node during singleplayer restore. " +
                $"Monster={snapshot.MonsterId ?? "unknown"}, Slot={snapshot.SlotName ?? "unknown"}: {e.Message}");
            return new EnsuredCreatureNodeResult(combatRoomNode.GetCreatureNode(creature), false);
        }
    }

    private static string DescribeCreatureNodeForLog(NCreature node)
    {
        return node.Entity != null
            ? DescribeCreatureForLog(node.Entity)
            : node.Name.ToString();
    }

    private static void RemoveCreatureNodeFromCombatRoom(NCombatRoom combatRoomNode, NCreature node)
    {
        RemoveCreatureNodeFromRoomList(combatRoomNode, NCombatRoomCreatureNodesField, node);
        RemoveCreatureNodeFromRoomList(combatRoomNode, NCombatRoomRemovingCreatureNodesField, node);
        ForceRemoveCreatureNodeImmediately(node);
    }

    private static void RemoveCreatureNodeFromRoomList(NCombatRoom combatRoomNode, FieldInfo? listField, NCreature node)
    {
        if (listField == null)
            return;

        try
        {
            if (listField.GetValue(combatRoomNode) is List<NCreature> nodes)
                nodes.Remove(node);
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"Failed to detach stale combat room creature node list {listField.Name}: {e.Message}");
        }
    }

    private static void ForceRemoveCreatureNodeImmediately(NCreature node)
    {
        try
        {
            node.Visible = false;
            var parent = node.GetParent();
            parent?.RemoveChild(node);
            node.QueueFree();
        }
        catch
        {
            // Best-effort cleanup only. The normal room-node removal path has already run.
        }
    }

    private static void TryRefreshLocalCombatUi(RunState runState, CombatState combatState, int roundNumber, string restoreContext)
    {
        try
        {
            ClearTransientLocalCombatCardUi();
            RebuildLocalHandUi(runState);
            RefreshLocalCombatPileUi(runState);
            RefreshCombatUiState(combatState);
            ShowPlayerTurnBannerIfNeeded(roundNumber);
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"Failed to refresh singleplayer combat UI for {restoreContext}: {e.Message}");
        }
    }

    private static async Task TryRefreshLocalCombatUiAfterDelayAsync(
        RunState runState,
        CombatRoom expectedCombatRoom,
        CombatState expectedCombatState,
        int roundNumber,
        string restoreContext)
    {
        await Task.Delay(FollowUpUiRefreshDelayMs);

        if (!TryGetReadyRestoreContext(runState, out var readyContext))
            return;

        if (readyContext.CombatRoom != expectedCombatRoom || readyContext.CombatState != expectedCombatState)
            return;

        TryRefreshLocalCombatUi(runState, expectedCombatState, roundNumber, $"{restoreContext} follow-up");
    }

    private static void RebuildLocalHandUi(RunState runState)
    {
        var handUi = NPlayerHand.Instance;
        var localPlayer = LocalContext.GetMe(runState);
        if (handUi == null || localPlayer?.PlayerCombatState == null)
            return;

        handUi.CancelAllCardPlay();

        foreach (var holder in handUi.ActiveHolders.ToList())
        {
            var cardModel = holder.CardModel;
            if (cardModel != null)
                handUi.Remove(cardModel);
        }

        var handPile = CardPile.Get(PileType.Hand, localPlayer);
        if (handPile == null)
            return;

        for (var index = 0; index < handPile.Cards.Count; index++)
        {
            var card = handPile.Cards[index];
            var cardNode = NCard.Create(card, ModelVisibility.Visible);
            if (cardNode == null)
                continue;

            handUi.Add(cardNode, index);
        }

        handUi.ForceRefreshCardIndices();
    }

    private static void ClearTransientLocalCombatCardUi()
    {
        var combatUi = NCombatRoom.Instance?.Ui;
        if (combatUi == null)
            return;

        ClearPlayQueueUi(combatUi.PlayQueue);
        ClearTransientCardNodes(combatUi.PlayContainer);
        ClearTransientCardNodes(NCombatRoom.Instance?.CombatVfxContainer);
        ClearTransientCardNodes(NRun.Instance?.GlobalUi?.TopBar?.TrailContainer);
    }

    private static void ClearPlayQueueUi(NCardPlayQueue? playQueue)
    {
        if (playQueue == null)
            return;

        try
        {
            if (NCardPlayQueueItemsField?.GetValue(playQueue) is IList queueItems)
            {
                foreach (var queueItem in queueItems.Cast<object>().ToList())
                {
                    if (NCardPlayQueueItemCurrentTweenField?.GetValue(queueItem) is Tween tween)
                        tween.Kill();

                    if (NCardPlayQueueItemCardField?.GetValue(queueItem) is NCard cardNode)
                        ForceRemoveCardNodeImmediately(cardNode);
                }

                queueItems.Clear();
            }
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"Failed to clear stale play-queue cards during singleplayer combat UI refresh: {e.Message}");
        }

        foreach (var cardNode in playQueue.GetChildren().OfType<NCard>().ToList())
            ForceRemoveCardNodeImmediately(cardNode);
    }

    private static void ClearTransientCardNodes(Node? container)
    {
        if (container == null)
            return;

        foreach (var cardNode in container.GetChildren().OfType<NCard>().ToList())
            ForceRemoveCardNodeImmediately(cardNode);
    }

    private static void RefreshLocalCombatPileUi(RunState runState)
    {
        var combatUi = NCombatRoom.Instance?.Ui;
        var localPlayer = LocalContext.GetMe(runState);
        if (combatUi == null || localPlayer?.PlayerCombatState == null)
            return;

        SyncCombatPileButtonState(combatUi.DrawPile, CardPile.Get(PileType.Draw, localPlayer));
        SyncCombatPileButtonState(combatUi.DiscardPile, CardPile.Get(PileType.Discard, localPlayer));
        SyncCombatPileButtonState(combatUi.ExhaustPile, CardPile.Get(PileType.Exhaust, localPlayer));
    }

    private static void SyncCombatPileButtonState(NCombatCardPile? pileButton, CardPile? pile)
    {
        if (pileButton == null || pile == null)
            return;

        var count = pile.Cards.Count;
        CombatCardPileCurrentCountField?.SetValue(pileButton, count);

        if (CombatCardPileCountLabelField?.GetValue(pileButton) is Control countLabel)
        {
            countLabel.GetType()
                .GetMethod("SetTextAutoSize", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, [typeof(string)])
                ?.Invoke(countLabel, [count.ToString()]);
            countLabel.PivotOffset = countLabel.Size * 0.5f;
        }

        if (pileButton is not NExhaustPileButton exhaustPileButton)
            return;

        if (count > 0)
        {
            exhaustPileButton.Visible = true;
            exhaustPileButton.Enable();
            return;
        }

        exhaustPileButton.Visible = false;
        exhaustPileButton.Disable();
    }

    private static void ForceRemoveCardNodeImmediately(NCard node)
    {
        try
        {
            node.Visible = false;
            node.GetParent()?.RemoveChild(node);
            node.QueueFree();
        }
        catch
        {
            // Best-effort cleanup only. The normal UI cleanup path has already run.
        }
    }

    private static void RefreshCombatUiState(CombatState combatState)
    {
        var handUi = NPlayerHand.Instance;
        if (handUi != null)
            HandOnCombatStateChangedMethod?.Invoke(handUi, [combatState]);

        var endTurnButton = NCombatRoom.Instance?.Ui?.EndTurnButton;
        if (endTurnButton != null && combatState.CurrentSide == CombatSide.Player)
            EndTurnButtonOnTurnStartedMethod?.Invoke(endTurnButton, [combatState]);
    }

    private static void ShowPlayerTurnBannerIfNeeded(int roundNumber)
    {
        if (roundNumber <= 1)
            return;

        var combatRoom = NCombatRoom.Instance;
        var banner = NPlayerTurnBanner.Create(roundNumber);
        if (combatRoom == null || banner == null)
            return;

        foreach (var child in combatRoom.GetChildren())
        {
            if (child is NPlayerTurnBanner existingBanner)
                existingBanner.QueueFree();
        }

        combatRoom.AddChild(banner);
    }

    private static void RegisterCardInCombatState(CardModel card)
    {
        CombatStateAddCardMethod?.Invoke(CombatStateCompatibilityService.GetRawCombatState(card.Owner?.Creature), [card]);
    }

    private static void RemoveCardFromRestoreState(CardModel card)
    {
        var combatState = CombatStateCompatibilityService.GetCombatState(card);
        var runState = card.RunState;

        try
        {
            card.RemoveFromCurrentPile(true);
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn(
                $"Failed to remove combat card from pile during singleplayer combat snapshot restore cleanup. " +
                $"Card={card.Id.Entry}, Owner={card.Owner?.NetId.ToString() ?? "unknown"}: {e.Message}");
        }

        card.HasBeenRemovedFromState = true;

        try
        {
            if (combatState?.ContainsCard(card) == true)
                combatState.RemoveCard(card);
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn(
                $"Failed to remove combat card from CombatState during singleplayer combat snapshot restore cleanup. " +
                $"Card={card.Id.Entry}, Owner={card.Owner?.NetId.ToString() ?? "unknown"}: {e.Message}");
        }

        try
        {
            if (runState?.ContainsCard(card) == true)
                runState.RemoveCard(card);
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn(
                $"Failed to remove combat card from RunState during singleplayer combat snapshot restore cleanup. " +
                $"Card={card.Id.Entry}, Owner={card.Owner?.NetId.ToString() ?? "unknown"}: {e.Message}");
        }
    }

    private static bool? GetHandCanPlayCardsState()
    {
        return InvokeBooleanMethod(HandCanPlayCardsMethod, NPlayerHand.Instance);
    }

    private static bool? GetHandActionsAllowedState()
    {
        return InvokeBooleanMethod(HandAreCardActionsAllowedMethod, NPlayerHand.Instance);
    }

    private static bool? InvokeBooleanMethod(MethodInfo? method, object? target)
    {
        if (method == null || target == null)
            return null;

        try
        {
            return method.Invoke(target, null) is bool value ? value : null;
        }
        catch
        {
            return null;
        }
    }

    private static Creature? ResolveCreature(CombatState combatState, CombatCreatureSnapshot snapshot)
    {
        return ResolveCreature(combatState, snapshot, null, null);
    }

    private static Creature? ResolveCreature(
        CombatState combatState,
        CombatCreatureSnapshot snapshot,
        SnapshotResolutionContext resolutionContext)
    {
        return ResolveCreature(combatState, snapshot, resolutionContext, null);
    }

    private static Creature? ResolveCreature(
        CombatState combatState,
        CombatCreatureSnapshot snapshot,
        ISet<Creature>? excludedCreatures)
    {
        return ResolveCreature(combatState, snapshot, null, excludedCreatures);
    }

    private static Creature? ResolveCreature(
        CombatState combatState,
        CombatCreatureSnapshot snapshot,
        SnapshotResolutionContext? resolutionContext,
        ISet<Creature>? excludedCreatures)
    {
        if (resolutionContext?.TryGetCreature(snapshot, out var resolvedCreature) == true)
            return excludedCreatures == null || !excludedCreatures.Contains(resolvedCreature) ? resolvedCreature : null;

        if (snapshot.PlayerId.HasValue)
        {
            return combatState.Creatures.FirstOrDefault(creature =>
                creature.Player?.NetId == snapshot.PlayerId.Value
                && (excludedCreatures == null || !excludedCreatures.Contains(creature)));
        }

        if (snapshot.CombatId.HasValue)
        {
            var byCombatId = combatState.Creatures.FirstOrDefault(creature =>
                creature.Player == null
                && creature.CombatId == snapshot.CombatId.Value
                && (excludedCreatures == null || !excludedCreatures.Contains(creature)));
            if (byCombatId != null)
                return byCombatId;
        }

        if (!string.IsNullOrWhiteSpace(snapshot.SlotName))
        {
            var bySlot = combatState.Creatures.FirstOrDefault(creature =>
                creature.Player == null
                && string.Equals(creature.SlotName, snapshot.SlotName, StringComparison.Ordinal)
                && (excludedCreatures == null || !excludedCreatures.Contains(creature)));
            if (bySlot != null)
                return bySlot;
        }

        var byMonsterId = combatState.Creatures
            .Where(creature => creature.Player == null && creature.Monster?.Id.Entry == snapshot.MonsterId)
            .Where(creature => excludedCreatures == null || !excludedCreatures.Contains(creature))
            .ToList();
        if (byMonsterId.Count == 0)
            return null;

        return snapshot.MonsterInstanceIndex >= 0 && snapshot.MonsterInstanceIndex < byMonsterId.Count
            ? byMonsterId[snapshot.MonsterInstanceIndex]
            : byMonsterId[0];
    }

    private static List<SpecialFieldSnapshot> CaptureSpecialMonsterFields(MonsterModel monster)
    {
        if (!SingleplayerPreviousStepSpecialSnapshotRegistry.TryGetMonsterPrivateFields(monster.Id.Entry, out var fieldNames))
            return [];

        return CaptureFields(monster, fieldNames);
    }

    private static List<SpecialFieldSnapshot> CaptureFields(object target, IReadOnlyList<string> fieldNames)
    {
        var snapshots = new List<SpecialFieldSnapshot>();
        var targetType = target.GetType();
        foreach (var fieldName in fieldNames)
        {
            var field = targetType.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (field == null)
                continue;

            var value = field.GetValue(target);
            if (value == null)
                continue;

            snapshots.Add(new SpecialFieldSnapshot
            {
                FieldName = fieldName,
                ValueJson = JsonSerializer.Serialize(value, field.FieldType)
            });
        }

        return snapshots;
    }

    private static bool TryReadSpecialFieldInt(IReadOnlyList<SpecialFieldSnapshot> fieldSnapshots, string fieldName, out int value)
    {
        value = default;

        var snapshot = fieldSnapshots.FirstOrDefault(field =>
            string.Equals(field.FieldName, fieldName, StringComparison.Ordinal));
        if (snapshot == null)
            return false;

        try
        {
            value = JsonSerializer.Deserialize<int>(snapshot.ValueJson);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryReadSpecialFieldBool(IReadOnlyList<SpecialFieldSnapshot> fieldSnapshots, string fieldName, out bool value)
    {
        value = default;

        var snapshot = fieldSnapshots.FirstOrDefault(field =>
            string.Equals(field.FieldName, fieldName, StringComparison.Ordinal));
        if (snapshot == null)
            return false;

        try
        {
            value = JsonSerializer.Deserialize<bool>(snapshot.ValueJson);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void TryRestoreField(object target, SpecialFieldSnapshot fieldSnapshot, string logContext)
    {
        var field = target.GetType().GetField(fieldSnapshot.FieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (field == null)
            return;

        try
        {
            var value = JsonSerializer.Deserialize(fieldSnapshot.ValueJson, field.FieldType);
            field.SetValue(target, value);
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"Failed to restore {logContext} field {fieldSnapshot.FieldName}: {e.Message}");
        }
    }

    private static void TryRestoreBooleanMember(
        object target,
        PropertyInfo? property,
        FieldInfo? fallbackField,
        bool? value,
        string logContext)
    {
        if (!value.HasValue)
            return;

        try
        {
            if (property?.CanWrite == true)
            {
                property.SetValue(target, value.Value);
                return;
            }

            if (fallbackField != null)
                fallbackField.SetValue(target, value.Value);
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"Failed to restore {logContext}: {e.Message}");
        }
    }

    public sealed class CombatStateSnapshot
    {
        public List<CombatCreatureSnapshot> Creatures { get; set; } = [];
        public List<CombatPlayerSnapshot> Players { get; set; } = [];
        public List<string> EncounterSlots { get; set; } = [];
        public SerializableRunRngSet Rng { get; set; } = new();
        public CombatManagerSnapshot? CombatManager { get; set; }
        public int RoundNumber { get; set; }
        public CombatSide CurrentSide { get; set; }
        public List<uint> NextChoiceIds { get; set; } = [];
        public uint? LastExecutedHookId { get; set; }
        public uint? LastExecutedActionId { get; set; }

        public static CombatStateSnapshot FromCurrentRun(RunState runState)
        {
            var netState = NetFullCombatState.FromRun(runState, null);
            var combatState = (runState.CurrentRoom as CombatRoom)?.CombatState;
            return new CombatStateSnapshot
            {
                Creatures = BuildCreatureSnapshots(runState, netState),
                Players = netState.Players
                    .Select(state => CombatPlayerSnapshot.FromNetState(state, runState.GetPlayer(state.playerId)))
                    .ToList(),
                EncounterSlots = CaptureEncounterSlots(runState),
                Rng = netState.Rng,
                CombatManager = CombatManagerSnapshot.Capture(),
                RoundNumber = combatState?.RoundNumber ?? 1,
                CurrentSide = combatState?.CurrentSide ?? CombatSide.Player,
                NextChoiceIds = netState.nextChoiceIds.ToList(),
                LastExecutedHookId = netState.lastExecutedHookId,
                LastExecutedActionId = netState.lastExecutedActionId
            };
        }

        private static List<CombatCreatureSnapshot> BuildCreatureSnapshots(RunState runState, NetFullCombatState netState)
        {
            var combatState = (runState.CurrentRoom as CombatRoom)?.CombatState;
            var snapshots = new List<CombatCreatureSnapshot>();
            var monsterCounters = new Dictionary<string, int>();

            for (var index = 0; index < netState.Creatures.Count; index++)
            {
                var state = netState.Creatures[index];
                var actualCreature = combatState != null && index < combatState.Creatures.Count
                    ? combatState.Creatures[index]
                    : null;

                if (state.playerId.HasValue)
                {
                    snapshots.Add(CombatCreatureSnapshot.FromNetState(state, actualCreature, 0));
                    continue;
                }

                var monsterId = state.monsterId?.Entry ?? actualCreature?.Monster?.Id.Entry ?? string.Empty;
                var monsterInstanceIndex = monsterCounters.TryGetValue(monsterId, out var currentIndex) ? currentIndex : 0;
                monsterCounters[monsterId] = monsterInstanceIndex + 1;

                snapshots.Add(CombatCreatureSnapshot.FromNetState(state, actualCreature, monsterInstanceIndex));
            }

            return snapshots;
        }

        private static List<string> CaptureEncounterSlots(RunState runState)
        {
            if (runState.CurrentRoom is not CombatRoom combatRoom)
                return [];

            var fromEncounterMonsters = combatRoom.Encounter?.MonstersWithSlots?
                .Select(entry => entry.Item2)
                .Where(slot => !string.IsNullOrWhiteSpace(slot))
                .Select(slot => slot!)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (fromEncounterMonsters is { Count: > 0 })
                return fromEncounterMonsters;

            var fromEncounterSlots = combatRoom.Encounter?.Slots?
                .Where(slot => !string.IsNullOrWhiteSpace(slot))
                .Select(slot => slot!)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (fromEncounterSlots is { Count: > 0 })
                return fromEncounterSlots;

            return combatRoom.CombatState?.Creatures
                .Where(creature => creature.Player == null && !string.IsNullOrWhiteSpace(creature.SlotName))
                .Select(creature => creature.SlotName!)
                .Distinct(StringComparer.Ordinal)
                .ToList()
                ?? [];
        }
    }

    public sealed class CombatManagerSnapshot
    {
        public SpecialFieldSnapshot? PlayerActionsDisabledField { get; set; }
        public SpecialFieldSnapshot? PlayersReadyToEndTurnField { get; set; }
        public SpecialFieldSnapshot? PlayersReadyToBeginEnemyTurnField { get; set; }
        public bool? IsPlayPhase { get; set; }
        public bool? IsEnemyTurnStarted { get; set; }
        public bool? EndingPlayerTurnPhaseOne { get; set; }
        public bool? EndingPlayerTurnPhaseTwo { get; set; }

        public static CombatManagerSnapshot? Capture()
        {
            var combatManager = CombatManager.Instance;
            if (combatManager == null)
                return null;

            return new CombatManagerSnapshot
            {
                PlayerActionsDisabledField = CaptureField(combatManager, CombatManagerPlayerActionsDisabledField),
                PlayersReadyToEndTurnField = CaptureField(combatManager, CombatManagerPlayersReadyToEndTurnField),
                PlayersReadyToBeginEnemyTurnField = CaptureField(combatManager, CombatManagerPlayersReadyToBeginEnemyTurnField),
                IsPlayPhase = CombatStateCompatibilityService.IsPlayPhase(CombatStateCompatibilityService.GetCurrentCombatState()),
                IsEnemyTurnStarted = TryReadBoolProperty(CombatManagerIsEnemyTurnStartedProperty, combatManager)
                    ?? TryReadNullableBoolField(CombatManagerIsEnemyTurnStartedField, combatManager),
                EndingPlayerTurnPhaseOne = TryReadBoolProperty(CombatManagerEndingPlayerTurnPhaseOneProperty, combatManager)
                    ?? TryReadNullableBoolField(CombatManagerEndingPlayerTurnPhaseOneField, combatManager),
                EndingPlayerTurnPhaseTwo = TryReadBoolProperty(CombatManagerEndingPlayerTurnPhaseTwoProperty, combatManager)
                    ?? TryReadNullableBoolField(CombatManagerEndingPlayerTurnPhaseTwoField, combatManager)
            };
        }

        private static SpecialFieldSnapshot? CaptureField(object target, FieldInfo? field)
        {
            if (field == null)
                return null;

            var value = field.GetValue(target);
            if (value == null)
                return null;

            return new SpecialFieldSnapshot
            {
                FieldName = field.Name,
                ValueJson = JsonSerializer.Serialize(value, field.FieldType)
            };
        }
    }

    public sealed class CombatCreatureSnapshot
    {
        public string? MonsterId { get; set; }
        public int MonsterInstanceIndex { get; set; }
        public uint? CombatId { get; set; }
        public string? SlotName { get; set; }
        public string? CurrentMoveFollowUpStateId { get; set; }
        public bool IsTemporaryStunned { get; set; }
        public ulong? PlayerId { get; set; }
        public int CurrentHp { get; set; }
        public int MaxHp { get; set; }
        public int Block { get; set; }
        public List<CombatPowerSnapshot> Powers { get; set; } = [];
        public string? CurrentMoveStateId { get; set; }
        public List<string> MoveStateLogIds { get; set; } = [];
        public bool PerformedFirstMove { get; set; }
        public List<SpecialFieldSnapshot> SpecialFields { get; set; } = [];
        public CombatVector2Snapshot? NodeGlobalPosition { get; set; }

        public static CombatCreatureSnapshot FromNetState(NetFullCombatState.CreatureState state, Creature? creature, int monsterInstanceIndex)
        {
            var moveStateMachine = creature?.Monster?.MoveStateMachine;
            var creatureNode = creature != null ? NCombatRoom.Instance?.GetCreatureNode(creature) : null;
            return new CombatCreatureSnapshot
            {
                MonsterId = state.monsterId?.Entry,
                MonsterInstanceIndex = monsterInstanceIndex,
                CombatId = creature?.CombatId,
                SlotName = creature?.SlotName,
                CurrentMoveFollowUpStateId = creature?.Monster?.NextMove?.FollowUpStateId,
                IsTemporaryStunned = string.Equals(creature?.Monster?.NextMove?.Id, "STUNNED", StringComparison.Ordinal),
                PlayerId = state.playerId,
                CurrentHp = state.currentHp,
                MaxHp = state.maxHp,
                Block = state.block,
                Powers = BuildPowerSnapshots(state, creature),
                CurrentMoveStateId = creature?.Monster?.NextMove?.Id,
                MoveStateLogIds = moveStateMachine?.StateLog.Select(move => move.Id).ToList() ?? [],
                PerformedFirstMove = TryReadBoolField(MoveStateMachinePerformedFirstMoveField, moveStateMachine),
                SpecialFields = creature?.Monster != null ? CaptureSpecialMonsterFields(creature.Monster) : [],
                NodeGlobalPosition = creature?.Monster is ToughEgg
                    ? CombatVector2Snapshot.FromVector2(creatureNode?.GlobalPosition)
                    : null
            };
        }

        private static List<CombatPowerSnapshot> BuildPowerSnapshots(NetFullCombatState.CreatureState state, Creature? creature)
        {
            var snapshots = new List<CombatPowerSnapshot>();
            for (var index = 0; index < state.powers.Count; index++)
            {
                var powerState = state.powers[index];
                var actualPower = creature != null && creature.Powers.Count > index ? creature.Powers[index] : null;
                snapshots.Add(CombatPowerSnapshot.FromNetState(powerState, actualPower));
            }

            return snapshots;
        }
    }

    private static bool TryReadBoolField(FieldInfo? field, object? target)
    {
        if (field == null || target == null)
            return false;

        try
        {
            return field.GetValue(target) is bool value && value;
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"Failed to read reflected bool field {field.Name}: {e.Message}");
            return false;
        }
    }

    private static bool? TryReadNullableBoolField(FieldInfo? field, object? target)
    {
        if (field == null || target == null)
            return null;

        try
        {
            return ReadBooleanMember(field.GetValue(target));
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"Failed to read reflected nullable bool field {field.Name}: {e.Message}");
            return null;
        }
    }

    private static bool? TryReadBoolProperty(PropertyInfo? property, object? target)
    {
        if (property == null || target == null)
            return null;

        try
        {
            return ReadBooleanMember(property.GetValue(target));
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"Failed to read reflected bool property {property.Name}: {e.Message}");
            return null;
        }
    }

    private static bool? ReadBooleanMember(object? value)
    {
        return value is bool boolValue ? boolValue : null;
    }

    public sealed class CombatPowerSnapshot
    {
        public ModelId Id { get; set; }
        public int Amount { get; set; }
        public ulong? TargetPlayerId { get; set; }
        public CombatCardSnapshot? SwipeStolenCard { get; set; }
        public List<SpecialFieldSnapshot> SpecialFields { get; set; } = [];

        public static CombatPowerSnapshot FromNetState(NetFullCombatState.PowerState state, PowerModel? actualPower)
        {
            var snapshot = new CombatPowerSnapshot
            {
                Id = state.id,
                Amount = state.amount
            };

            if (actualPower is ThieveryPower thieveryPower)
            {
                snapshot.TargetPlayerId = thieveryPower.Target?.Player?.NetId;
            }
            else if (actualPower is SwipePower swipePower)
            {
                snapshot.TargetPlayerId = swipePower.Target?.Player?.NetId;
                snapshot.SwipeStolenCard = CombatCardSnapshot.FromCard(swipePower.StolenCard);
            }
            else if (actualPower is SandpitPower sandpitPower)
            {
                snapshot.TargetPlayerId = sandpitPower.Target?.Player?.NetId;
            }

            if (actualPower != null
                && SingleplayerPreviousStepSpecialSnapshotRegistry.TryGetPowerPrivateFields(actualPower.Id.Entry, out var powerFieldNames))
            {
                snapshot.SpecialFields = CaptureFields(actualPower, powerFieldNames);
            }

            return snapshot;
        }
    }

    public sealed class CombatVector2Snapshot
    {
        public float X { get; set; }
        public float Y { get; set; }

        public static CombatVector2Snapshot? FromVector2(Vector2? value)
        {
            if (!value.HasValue)
                return null;

            return new CombatVector2Snapshot
            {
                X = value.Value.X,
                Y = value.Value.Y
            };
        }

        public Vector2 ToVector2()
        {
            return new Vector2(X, Y);
        }
    }

    public sealed class CombatPlayerSnapshot
    {
        public ulong PlayerId { get; set; }
        public ModelId CharacterId { get; set; }
        public int Energy { get; set; }
        public int Stars { get; set; }
        public int MaxStars { get; set; }
        public int MaxPotionCount { get; set; }
        public int Gold { get; set; }
        public List<CombatPileSnapshot> Piles { get; set; } = [];
        public List<CombatPotionSnapshot> Potions { get; set; } = [];
        public List<CombatRelicSnapshot> Relics { get; set; } = [];
        public List<CombatOrbSnapshot> Orbs { get; set; } = [];
        public SerializablePlayerRngSet RngSet { get; set; } = new();
        public SerializablePlayerOddsSet OddsSet { get; set; } = new();
        public SerializableRelicGrabBag RelicGrabBag { get; set; } = new();

        public static CombatPlayerSnapshot FromNetState(NetFullCombatState.PlayerState state, Player? actualPlayer)
        {
            return new CombatPlayerSnapshot
            {
                PlayerId = state.playerId,
                CharacterId = state.characterId,
                Energy = state.energy,
                Stars = state.stars,
                MaxStars = state.maxStars,
                MaxPotionCount = state.maxPotionCount,
                Gold = state.gold,
                Piles = state.piles.Select(pileState =>
                    CombatPileSnapshot.FromNetState(
                        pileState,
                        actualPlayer != null ? CardPile.Get(pileState.pileType, actualPlayer) : null))
                    .ToList(),
                Potions = state.potions.Select(CombatPotionSnapshot.FromNetState).ToList(),
                Relics = state.relics.Select(CombatRelicSnapshot.FromNetState).ToList(),
                Orbs = state.orbs.Select(CombatOrbSnapshot.FromNetState).ToList(),
                RngSet = state.rngSet,
                OddsSet = ResolveOddsSet(state.playerId),
                RelicGrabBag = state.relicGrabBag
            };
        }

        private static SerializablePlayerOddsSet ResolveOddsSet(ulong playerId)
        {
            return RunManager.Instance.DebugOnlyGetState()?.GetPlayer(playerId)?.PlayerOdds.ToSerializable()
                ?? new SerializablePlayerOddsSet();
        }
    }

    public sealed class CombatPileSnapshot
    {
        public PileType PileType { get; set; }
        public List<CombatCardSnapshot> Cards { get; set; } = [];

        public static CombatPileSnapshot FromNetState(NetFullCombatState.CombatPileState state, CardPile? actualPile)
        {
            var cards = new List<CombatCardSnapshot>();
            for (var index = 0; index < state.cards.Count; index++)
            {
                var actualCard = actualPile != null && index < actualPile.Cards.Count
                    ? actualPile.Cards[index]
                    : null;
                cards.Add(CombatCardSnapshot.FromNetState(state.cards[index], actualCard));
            }

            return new CombatPileSnapshot
            {
                PileType = state.pileType,
                Cards = cards
            };
        }
    }

    public sealed class CombatCardSnapshot
    {
        public SerializableCard Card { get; set; } = new();
        public bool HadDeckVersion { get; set; }
        public SerializableCard? DeckVersionCard { get; set; }
        public ModelId? Affliction { get; set; }
        public int AfflictionCount { get; set; }
        public List<CardKeyword>? Keywords { get; set; }

        public static CombatCardSnapshot FromNetState(NetFullCombatState.CardState state, CardModel? actualCard)
        {
            return new CombatCardSnapshot
            {
                Card = state.card,
                HadDeckVersion = actualCard?.DeckVersion != null,
                DeckVersionCard = actualCard?.DeckVersion?.ToSerializable(),
                Affliction = state.affliction,
                AfflictionCount = state.afflictionCount,
                Keywords = state.keywords?.ToList()
            };
        }

        public static CombatCardSnapshot? FromCard(CardModel? card)
        {
            if (card == null)
                return null;

            return new CombatCardSnapshot
            {
                Card = card.ToSerializable(),
                HadDeckVersion = card.DeckVersion != null,
                DeckVersionCard = card.DeckVersion?.ToSerializable(),
                Affliction = card.Affliction?.Id,
                AfflictionCount = card.Affliction?.Amount ?? 0,
                Keywords = card.Keywords.ToList()
            };
        }
    }

    public sealed class CombatPotionSnapshot
    {
        public ModelId Id { get; set; }

        public static CombatPotionSnapshot FromNetState(NetFullCombatState.PotionState state)
        {
            return new CombatPotionSnapshot
            {
                Id = state.id
            };
        }
    }

    public sealed class CombatRelicSnapshot
    {
        public SerializableRelic Relic { get; set; } = new();

        public static CombatRelicSnapshot FromNetState(NetFullCombatState.RelicState state)
        {
            return new CombatRelicSnapshot
            {
                Relic = state.relic
            };
        }
    }

    public sealed class CombatOrbSnapshot
    {
        public ModelId Id { get; set; }
        public int Passive { get; set; }
        public int Evoke { get; set; }

        public static CombatOrbSnapshot FromNetState(NetFullCombatState.OrbState state)
        {
            return new CombatOrbSnapshot
            {
                Id = state.id,
                Passive = state.passive,
                Evoke = state.evoke
            };
        }
    }

    public sealed class SpecialFieldSnapshot
    {
        public string FieldName { get; set; } = string.Empty;
        public string ValueJson { get; set; } = string.Empty;
    }

    private sealed class SnapshotResolutionContext
    {
        private readonly Dictionary<CombatCreatureSnapshot, Creature> _creaturesBySnapshot = [];

        public void Register(CombatCreatureSnapshot snapshot, Creature creature)
        {
            _creaturesBySnapshot[snapshot] = creature;
        }

        public bool TryGetCreature(CombatCreatureSnapshot snapshot, out Creature creature)
        {
            return _creaturesBySnapshot.TryGetValue(snapshot, out creature!);
        }

        public IReadOnlyList<ResolvedCreatureSnapshot> GetResolvedSnapshots(string monsterId)
        {
            return _creaturesBySnapshot
                .Where(entry => string.Equals(entry.Key.MonsterId, monsterId, StringComparison.Ordinal))
                .Select(entry => new ResolvedCreatureSnapshot(entry.Key, entry.Value))
                .ToList();
        }
    }

    private readonly record struct EnsuredCreatureNodeResult(NCreature? Node, bool WasRecreated);
    private readonly record struct ResolvedCreatureSnapshot(CombatCreatureSnapshot Snapshot, Creature Creature);
    private readonly record struct ReadySingleplayerCombatRestoreContext(CombatRoom CombatRoom, CombatState CombatState);
}
