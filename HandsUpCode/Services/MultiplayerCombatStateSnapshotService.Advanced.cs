using System;
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
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.Nodes.Vfx.Backgrounds;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace HandsUp.HandsUpCode.Services;

public static partial class MultiplayerCombatStateSnapshotService
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
    private const string KnowledgeDemonPonderMoveId = "PONDER_MOVE";
    private const string KnowledgeDemonIdleLoopAnimationId = "idle_loop";
    private const string KnowledgeDemonBurntLoopAnimationId = "burnt_loop";
    private const string KnowledgeDemonBurningStartMethod = "OnBurningStart";
    private const string KnowledgeDemonBurningEndMethod = "OnBurningEnd";
    private const string OwlMagistrateVerdictMoveId = "VERDICT";
    private const string OwlMagistrateTakeOffTrigger = "TakeOff";
    private const string OwlMagistrateFlyLoopAnimationId = "fly_loop";
    private const string OwlMagistrateFlyingBoundsContainer = "FlyingBounds";
    private const string RocketLaserMoveId = "LASER_MOVE";
    private const string RocketRechargeMoveId = "RECHARGE_MOVE";
    private const string TestSubjectRespawnMoveId = "RESPAWN_MOVE";
    private const string TestSubjectIdleLoop1AnimationId = "idle_loop1";
    private const string TestSubjectIdleLoop2AnimationId = "idle_loop2";
    private const string TestSubjectIdleLoop3AnimationId = "idle_loop3";
    private const string TestSubjectKnockedOutLoop1AnimationId = "knocked_out_loop1";
    private const string TestSubjectKnockedOutLoop2AnimationId = "knocked_out_loop2";
    private const string TestSubjectDieAnimationId = "die";
    private const int KaiserCrabRightArmTrack = 2;
    private const string NecrobinderCharacterId = "CHARACTER.NECROBINDER";
    private const string OstyMonsterId = "OSTY";

    // Multiplayer older previous-step snapshots did not persist pet owner ids.
    // Keep a narrow fallback map here so pet restores do not regress into enemy-side creatures.
    private static readonly Dictionary<string, string> KnownPetOwnerCharacterIdsByMonsterId = new(StringComparer.Ordinal)
    {
        [OstyMonsterId] = NecrobinderCharacterId
    };

    private static readonly MethodInfo? HandCanPlayCardsMethod = typeof(NPlayerHand)
        .GetMethod("CanPlayCards", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly MethodInfo? HandAreCardActionsAllowedMethod = typeof(NPlayerHand)
        .GetMethod("AreCardActionsAllowed", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly MethodInfo? MonsterModelNextMoveSetter = typeof(MonsterModel)
        .GetProperty("NextMove", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?
        .GetSetMethod(true);
    private static readonly FieldInfo? MoveStateMachineCurrentStateField = typeof(MonsterMoveStateMachine)
        .GetField("_currentState", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? MoveStateMachinePerformedFirstMoveField = typeof(MonsterMoveStateMachine)
        .GetField("_performedFirstMove", BindingFlags.Instance | BindingFlags.NonPublic);
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
    private static readonly PropertyInfo? TenderPowerCardsPlayedThisTurnProperty = typeof(TenderPower)
        .GetProperty("CardsPlayedThisTurn", BindingFlags.Instance | BindingFlags.NonPublic);
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
    private static readonly FieldInfo? TenderPowerCardsPlayedThisTurnField = typeof(TenderPower)
        .GetField("_cardsPlayedThisTurn", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? PowerModelInternalDataField = typeof(PowerModel)
        .GetField("_internalData", BindingFlags.Instance | BindingFlags.NonPublic);

    private static bool TryGetReadyRestoreContext(RunState runState, out ReadyMultiplayerCombatRestoreContext readyContext)
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

        readyContext = new ReadyMultiplayerCombatRestoreContext(combatRoom, combatState);
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

            if (creatureSnapshot.MonsterId == null)
                continue;

            try
            {
                var recreatedCreature = await RecreateMissingSnapshotCreatureAsync(combatState, creatureSnapshot, restoreContext);
                if (recreatedCreature == null)
                    continue;

                resolvedCreatures.Add(recreatedCreature);
                recreatedCreatures.Add(DescribeSnapshotCreatureForLog(creatureSnapshot));
            }
            catch (Exception e)
            {
                MainFile.Logger.Warn(
                    $"Failed to recreate creature missing from multiplayer combat snapshot for {restoreContext}. " +
                    $"Monster={creatureSnapshot.MonsterId?.Entry ?? "unknown"}, Slot={creatureSnapshot.SlotName ?? "unknown"}: {e.Message}");
            }
        }

        if (recreatedCreatures.Count == 0)
            return;

        if (snapshot.EncounterSlots.Count > 0)
            combatState.SortEnemiesBySlotName();

        MainFile.Logger.Info(
            $"Recreated {recreatedCreatures.Count} creature(s) missing from multiplayer combat snapshot for {restoreContext}: " +
            $"{string.Join(", ", recreatedCreatures)}");
    }

    private static async Task<Creature?> RecreateMissingSnapshotCreatureAsync(
        CombatState combatState,
        CombatCreatureSnapshot creatureSnapshot,
        string restoreContext)
    {
        if (creatureSnapshot.MonsterId == null)
            return null;

        var monster = SaveUtil.MonsterOrDeprecated(creatureSnapshot.MonsterId).ToMutable();
        if (TryResolvePetOwnerPlayer(combatState, creatureSnapshot.MonsterId, creatureSnapshot.PetOwnerPlayerId, out var petOwner)
            && petOwner?.PlayerCombatState != null)
        {
            if (!creatureSnapshot.PetOwnerPlayerId.HasValue)
            {
                MainFile.Logger.Info(
                    $"Inferred missing multiplayer pet owner {petOwner.NetId} while recreating snapshot creature " +
                    $"{DescribeSnapshotCreatureForLog(creatureSnapshot)} for {restoreContext}.");
            }

            var pet = combatState.CreateCreature(monster, petOwner.Creature.Side, creatureSnapshot.SlotName);
            petOwner.PlayerCombatState.AddPetInternal(pet);
            await CreatureCmd.Add(pet);
            return pet;
        }

        if (creatureSnapshot.PetOwnerPlayerId.HasValue)
        {
            MainFile.Logger.Warn(
                $"Failed to resolve pet owner {creatureSnapshot.PetOwnerPlayerId.Value} while recreating multiplayer snapshot creature " +
                $"{DescribeSnapshotCreatureForLog(creatureSnapshot)} for {restoreContext}. Falling back to enemy-side recreation.");
        }

        return await CreatureCmd.Add(monster, combatState, CombatSide.Enemy, creatureSnapshot.SlotName);
    }

    private static void RestoreCreatureStates(
        CombatState combatState,
        IReadOnlyList<CombatCreatureSnapshot> snapshots,
        string restoreContext,
        SnapshotResolutionContext resolutionContext)
    {
        foreach (var snapshot in snapshots)
        {
            var creature = ResolveCreature(combatState, snapshot, resolutionContext);
            if (creature == null)
            {
                MainFile.Logger.Warn(
                    $"Skipped multiplayer combat creature snapshot restore because the target creature could not be resolved. " +
                    $"Monster={snapshot.MonsterId?.Entry ?? "unknown"}, Index={snapshot.MonsterInstanceIndex}, Slot={snapshot.SlotName}, Player={snapshot.PlayerId}");
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
                    PrimeGremlinMercThieveryTargetBeforeApply(creature, power, powerSnapshot, combatState, restoreContext);
                    power.ApplyInternal(creature, powerSnapshot.Amount, true);
                }
                catch (Exception e)
                {
                    MainFile.Logger.Warn(
                        $"Skipped restoring multiplayer power {powerSnapshot.Id} x{powerSnapshot.Amount} for creature " +
                        $"{(snapshot.PlayerId?.ToString() ?? snapshot.MonsterId?.Entry ?? "unknown")} because restore failed: {e.Message}");
                }
            }
        }
    }

    private static void PrimeGremlinMercThieveryTargetBeforeApply(
        Creature creature,
        PowerModel power,
        CombatPowerSnapshot powerSnapshot,
        CombatState combatState,
        string restoreContext)
    {
        if (creature.Monster is not GremlinMerc
            || power is not ThieveryPower thieveryPower
            || !powerSnapshot.TargetPlayerId.HasValue)
        {
            return;
        }

        var targetPlayer = combatState.RunState?.GetPlayer(powerSnapshot.TargetPlayerId.Value);
        if (targetPlayer?.Creature == null)
        {
            MainFile.Logger.Warn(
                $"Skipped priming thievery target before multiplayer power restore for {restoreContext} " +
                $"because player {powerSnapshot.TargetPlayerId.Value} was not found.");
            return;
        }

        // Gremlin Merc's thievery powers are visibility-gated by Target. Set it before ApplyInternal
        // so the local power container only creates the single player-specific icon that should exist.
        thieveryPower.Target = targetPlayer.Creature;
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
                TryRestoreField(monster, fieldSnapshot, $"monster {snapshot.MonsterId?.Entry ?? "unknown"}");

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
                    $"Skipped restoring multiplayer move state {snapshot.CurrentMoveStateId} for monster {snapshot.MonsterId?.Entry ?? "unknown"} " +
                    $"because it was not present after restore. Slot={snapshot.SlotName}, TemporaryStunned={snapshot.IsTemporaryStunned}");
            }

            RestoreFabricatorState(monster, snapshot, restoreContext);
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
                $"Skipped restoring multiplayer move state {snapshot.CurrentMoveStateId ?? "unknown"} for monster " +
                $"{snapshot.MonsterId?.Entry ?? creature?.Monster?.Id.Entry ?? "unknown"} because NextMove setter could not be reflected during {restoreContext}.");
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
                $"Monster={snapshot.MonsterId?.Entry ?? "unknown"}, Slot={snapshot.SlotName}, FollowUp={snapshot.CurrentMoveFollowUpStateId ?? "null"}");
            return true;
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn(
                $"Failed to recreate illusion revive move state for {restoreContext}. " +
                $"Monster={snapshot.MonsterId?.Entry ?? "unknown"}, Slot={snapshot.SlotName}, FollowUp={snapshot.CurrentMoveFollowUpStateId ?? "null"}: {e.Message}");
            return false;
        }
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
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn(
                $"Failed to recreate temporary stunned move state for {restoreContext}. " +
                $"Monster={snapshot.MonsterId?.Entry ?? "unknown"}, Slot={snapshot.SlotName}, FollowUp={snapshot.CurrentMoveFollowUpStateId ?? "null"}: {e.Message}");
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
                $"{snapshot.MonsterId?.Entry ?? creature?.Monster?.Id.Entry ?? "unknown"} at slot {snapshot.SlotName ?? "unknown"}.");
        }
    }

    private static async Task TrySynchronizeSpecialMonsterStateAsync(
        Creature? creature,
        CombatCreatureSnapshot snapshot,
        string restoreContext,
        bool refreshIntentDisplays = true)
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
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn(
                $"Failed to synchronize hatched tough egg visuals during multiplayer combat snapshot restore for {restoreContext}. " +
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
                return;

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
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn(
                $"Failed to synchronize Knowledge Demon visual state during multiplayer combat snapshot restore for {restoreContext}. " +
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
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn(
                $"Failed to synchronize Kaiser Crab rocket combat state during multiplayer combat snapshot restore for {restoreContext}. " +
                $"Slot={snapshot.SlotName ?? "unknown"} Move={snapshot.CurrentMoveStateId ?? "unknown"}: {e.Message}");
        }
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
                return;

            var isPortalOpen = TryReadSpecialFieldBool(snapshot.SpecialFields, "_isPortalOpen", out var restoredPortalOpen)
                ? restoredPortalOpen
                : ResolveDoormakerPortalOpenFallback(creature, snapshot);

            creature.ShowsInfiniteHp = !isPortalOpen;

            if (isPortalOpen)
            {
                var visualPath = ResolveDoormakerVisualPath(creature, snapshot);
                if (!string.IsNullOrWhiteSpace(visualPath) && DoormakerUpdateVisualMethod != null)
                    DoormakerUpdateVisualMethod.Invoke(doormaker, [visualPath]);
            }

            creatureNode.GetNodeOrNull<Node>("%HealthBar")?.Call("RefreshValues");
            if (refreshIntentDisplays)
                await creatureNode.RefreshIntents();
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn(
                $"Failed to synchronize Doormaker phase state during multiplayer combat snapshot restore for {restoreContext}. " +
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
                return;

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
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn(
                $"Failed to synchronize Owl Magistrate flying state during multiplayer combat snapshot restore for {restoreContext}. " +
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
                return;

            var respawns = TryReadSpecialFieldInt(snapshot.SpecialFields, "_respawns", out var restoredRespawns)
                ? Math.Max(0, restoredRespawns)
                : 0;
            var animationId = ResolveTestSubjectAnimationId(snapshot, respawns);
            var spineBody = creatureNode.Visuals.SpineBody;
            if (!spineBody.HasAnimation(animationId))
                return;

            creatureNode.SetDefaultScaleTo(1f + respawns * 0.1f, 0f);
            creatureNode.SpineAnimation.SetAnimation(
                animationId,
                !string.Equals(animationId, TestSubjectDieAnimationId, StringComparison.Ordinal),
                0);
            if (refreshIntentDisplays)
                await creatureNode.RefreshIntents();
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn(
                $"Failed to synchronize Test Subject visual state during multiplayer combat snapshot restore for {restoreContext}. " +
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
                return;

            var visuals = background.GetNodeOrNull<Node2D>("%Visuals");
            if (visuals == null)
                return;

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
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn(
                $"Failed to synchronize Kaiser Crab rocket visual state during multiplayer combat snapshot restore for {restoreContext}. " +
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

            foreach (var (powerSnapshot, restoredPower) in MatchPowerSnapshotsToRestoredPowers(creature, snapshot.Powers, restoreContext))
            {
                RestorePowerDynamicVars(restoredPower, powerSnapshot, restoreContext);
                RestorePowerApplier(restoredPower, powerSnapshot, combatState, restoreContext);
                RestorePowerTarget(restoredPower, powerSnapshot, combatState, restoreContext);
                RestoreSwipePowerState(restoredPower, powerSnapshot, runState, combatState, usedDeckCardsByPlayer, restoreContext);

                foreach (var fieldSnapshot in powerSnapshot.SpecialFields)
                    TryRestoreField(restoredPower, fieldSnapshot, $"power {powerSnapshot.Id}");

                RestoreThieveryPowerState(restoredPower, powerSnapshot, restoreContext);
                RestoreTenderPowerState(restoredPower, powerSnapshot, restoreContext);
                RestoreComplexPowerState(runState, combatState, restoredPower, powerSnapshot, usedDeckCardsByPlayer, restoreContext);
            }
        }
    }

    private static void RestorePowerApplier(PowerModel restoredPower, CombatPowerSnapshot powerSnapshot, CombatState combatState, string restoreContext)
    {
        if (powerSnapshot.Applier == null)
            return;

        var applier = ResolveCreatureReference(combatState, powerSnapshot.Applier);
        if (applier == null)
        {
            MainFile.Logger.Warn(
                $"Skipped restoring applier for power {powerSnapshot.Id} during multiplayer combat snapshot restore for {restoreContext} " +
                $"because creature {DescribeCreatureReferenceForLog(powerSnapshot.Applier)} was not found.");
            return;
        }

        try
        {
            restoredPower.Applier = applier;
            TryRestorePowerApplierDisplay(restoredPower, restoreContext);
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn(
                $"Failed to restore applier for power {powerSnapshot.Id} during multiplayer combat snapshot restore for {restoreContext}: {e.Message}");
        }
    }

    private static void TryRestorePowerApplierDisplay(PowerModel restoredPower, string restoreContext)
    {
        var applierPlayer = restoredPower.Applier?.Player;
        if (applierPlayer == null)
            return;

        if (!restoredPower.DynamicVars.TryGetValue("Applier", out var dynamicVar) || dynamicVar is not StringVar stringVar)
            return;

        try
        {
            stringVar.StringValue = PlatformUtil.GetPlayerName(RunManager.Instance.NetService.Platform, applierPlayer.NetId);
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn(
                $"Failed to refresh applier display text for power {restoredPower.Id} during multiplayer combat snapshot restore for {restoreContext}: {e.Message}");
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
                $"Skipped restoring target for power {powerSnapshot.Id} during multiplayer combat snapshot restore for {restoreContext} " +
                $"because player {powerSnapshot.TargetPlayerId.Value} was not found.");
            return;
        }

        switch (restoredPower)
        {
            case ThieveryPower thieveryPower:
                thieveryPower.Target = targetPlayer.Creature;
                break;
            case HeistPower heistPower:
                heistPower.Target = targetPlayer.Creature;
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
                $"Skipped restoring stolen card for swipe power during multiplayer combat snapshot restore for {restoreContext} " +
                $"because player {powerSnapshot.TargetPlayerId?.ToString() ?? "unknown"} was not found.");
            swipePower.StolenCard = null;
            return;
        }

        var usedDeckCards = GetOrCreateUsedDeckCards(usedDeckCardsByPlayer, targetPlayer);
        swipePower.StolenCard = RestoreDetachedCardFromSnapshot(runState, targetPlayer, powerSnapshot.SwipeStolenCard, usedDeckCards, restoreContext);
    }

    private static void RestoreThieveryPowerState(PowerModel restoredPower, CombatPowerSnapshot powerSnapshot, string restoreContext)
    {
        if (restoredPower is not ThieveryPower thieveryPower || !powerSnapshot.ThieveryStolenGold.HasValue)
            return;

        try
        {
            thieveryPower.DynamicVars.Gold.BaseValue = powerSnapshot.ThieveryStolenGold.Value;
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn(
                $"Failed to restore stolen gold for thievery power during multiplayer combat snapshot restore for {restoreContext}: {e.Message}");
        }
    }

    private static void RestoreTenderPowerState(PowerModel restoredPower, CombatPowerSnapshot powerSnapshot, string restoreContext)
    {
        if (restoredPower is not TenderPower tenderPower || !powerSnapshot.TenderCardsPlayedThisTurn.HasValue)
            return;

        try
        {
            if (TenderPowerCardsPlayedThisTurnProperty?.CanWrite == true)
            {
                TenderPowerCardsPlayedThisTurnProperty.SetValue(tenderPower, powerSnapshot.TenderCardsPlayedThisTurn.Value);
                return;
            }

            if (TenderPowerCardsPlayedThisTurnField != null)
                TenderPowerCardsPlayedThisTurnField.SetValue(tenderPower, powerSnapshot.TenderCardsPlayedThisTurn.Value);
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn(
                $"Failed to restore cards-played count for tender power during multiplayer combat snapshot restore for {restoreContext}: {e.Message}");
        }
    }

    private static void RestorePowerDynamicVars(PowerModel restoredPower, CombatPowerSnapshot powerSnapshot, string restoreContext)
    {
        if (powerSnapshot.DynamicVars.Count == 0)
            return;

        foreach (var dynamicVarSnapshot in powerSnapshot.DynamicVars)
        {
            if (!restoredPower.DynamicVars.TryGetValue(dynamicVarSnapshot.Name, out var dynamicVar))
                continue;

            try
            {
                dynamicVar.BaseValue = dynamicVarSnapshot.BaseValue;
                if (dynamicVar is StringVar stringVar && dynamicVarSnapshot.HasStringValue)
                    stringVar.StringValue = dynamicVarSnapshot.StringValue ?? string.Empty;
            }
            catch (Exception e)
            {
                MainFile.Logger.Warn(
                    $"Failed to restore dynamic var {dynamicVarSnapshot.Name} for power {powerSnapshot.Id} during multiplayer combat snapshot restore for {restoreContext}: {e.Message}");
            }
        }
    }

    private static void RestoreComplexPowerState(
        RunState runState,
        CombatState combatState,
        PowerModel restoredPower,
        CombatPowerSnapshot powerSnapshot,
        IDictionary<ulong, HashSet<CardModel>> usedDeckCardsByPlayer,
        string restoreContext)
    {
        RestoreNightmarePowerState(runState, combatState, restoredPower, powerSnapshot, usedDeckCardsByPlayer, restoreContext);
        RestoreVitalSparkPowerState(combatState, restoredPower, powerSnapshot, restoreContext);
        RestoreCreatureDecimalMapPowerState(combatState, restoredPower, powerSnapshot, restoreContext);
        RestoreCardIntMapPowerState(combatState, restoredPower, powerSnapshot, restoreContext);
        RestoreDampenPowerState(combatState, restoredPower, powerSnapshot, restoreContext);
    }

    private static void RestoreNightmarePowerState(
        RunState runState,
        CombatState combatState,
        PowerModel restoredPower,
        CombatPowerSnapshot powerSnapshot,
        IDictionary<ulong, HashSet<CardModel>> usedDeckCardsByPlayer,
        string restoreContext)
    {
        if (restoredPower is not NightmarePower nightmarePower || powerSnapshot.NightmareSelectedCard == null)
            return;

        var player = nightmarePower.Owner?.Player;
        if (player == null)
        {
            MainFile.Logger.Warn(
                $"Skipped restoring nightmare selected card during multiplayer combat snapshot restore for {restoreContext} because owner player was not found.");
            return;
        }

        var usedDeckCards = GetOrCreateUsedDeckCards(usedDeckCardsByPlayer, player);
        var restoredCard = RestoreDetachedCardFromSnapshot(runState, player, powerSnapshot.NightmareSelectedCard, usedDeckCards, restoreContext);
        if (restoredCard == null)
            return;

        try
        {
            nightmarePower.SetSelectedCard(restoredCard);
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn(
                $"Failed to restore nightmare selected card during multiplayer combat snapshot restore for {restoreContext}: {e.Message}");
        }
    }

    private static void RestoreVitalSparkPowerState(
        CombatState combatState,
        PowerModel restoredPower,
        CombatPowerSnapshot powerSnapshot,
        string restoreContext)
    {
        if (restoredPower is not VitalSparkPower || powerSnapshot.VitalSparkTriggeredPlayerIds == null)
            return;

        if (!TryGetPowerInternalDataFieldValue<ISet<Player>>(restoredPower, "playersTriggeredThisTurn", out var triggeredPlayers)
            || triggeredPlayers == null)
        {
            return;
        }

        triggeredPlayers.Clear();
        foreach (var playerId in powerSnapshot.VitalSparkTriggeredPlayerIds)
        {
            var player = combatState.RunState?.GetPlayer(playerId);
            if (player != null)
            {
                triggeredPlayers.Add(player);
                continue;
            }

            MainFile.Logger.Warn(
                $"Skipped restoring vital spark triggered player {playerId} during multiplayer combat snapshot restore for {restoreContext} because the player was not found.");
        }
    }

    private static void RestoreCreatureDecimalMapPowerState(
        CombatState combatState,
        PowerModel restoredPower,
        CombatPowerSnapshot powerSnapshot,
        string restoreContext)
    {
        if (powerSnapshot.CreatureDecimalEntries == null || powerSnapshot.CreatureDecimalEntries.Count == 0)
            return;

        var fieldName = restoredPower switch
        {
            PossessStrengthPower => "stolenStrength",
            PossessSpeedPower => "stolenDexterity",
            _ => null
        };
        if (fieldName == null)
            return;

        if (!TryGetPowerInternalDataFieldValue<IDictionary<Creature, decimal>>(restoredPower, fieldName, out var valueMap)
            || valueMap == null)
        {
            return;
        }

        valueMap.Clear();
        foreach (var entry in powerSnapshot.CreatureDecimalEntries)
        {
            if (entry.Creature == null)
                continue;

            var creature = ResolveCreatureReference(combatState, entry.Creature);
            if (creature != null)
            {
                valueMap[creature] = entry.Value;
                continue;
            }

            MainFile.Logger.Warn(
                $"Skipped restoring {fieldName} entry during multiplayer combat snapshot restore for {restoreContext} because creature {DescribeCreatureReferenceForLog(entry.Creature)} was not found.");
        }
    }

    private static void RestoreCardIntMapPowerState(
        CombatState combatState,
        PowerModel restoredPower,
        CombatPowerSnapshot powerSnapshot,
        string restoreContext)
    {
        if (powerSnapshot.CardIntEntries == null || powerSnapshot.CardIntEntries.Count == 0)
            return;

        if (!TryGetTrackedCardIntMapFieldName(restoredPower, out var fieldName))
            return;

        if (!TryGetPowerInternalDataFieldValue<IDictionary<CardModel, int>>(restoredPower, fieldName, out var valueMap)
            || valueMap == null)
        {
            return;
        }

        valueMap.Clear();
        foreach (var entry in powerSnapshot.CardIntEntries)
        {
            var card = ResolveLiveCardReference(combatState, entry.Card);
            if (card != null)
            {
                valueMap[card] = entry.Value;
                continue;
            }

            MainFile.Logger.Warn(
                $"Skipped restoring pending card entry for power {powerSnapshot.Id} during multiplayer combat snapshot restore for {restoreContext} because the card reference could not be resolved.");
        }
    }

    private static void RestoreDampenPowerState(
        CombatState combatState,
        PowerModel restoredPower,
        CombatPowerSnapshot powerSnapshot,
        string restoreContext)
    {
        if (restoredPower is not DampenPower || powerSnapshot.DampenCasterReferences == null)
            return;

        if (!TryGetPowerInternalDataFieldValue<ISet<Creature>>(restoredPower, "casters", out var casters)
            || casters == null)
        {
            return;
        }

        casters.Clear();
        foreach (var reference in powerSnapshot.DampenCasterReferences)
        {
            var creature = ResolveCreatureReference(combatState, reference);
            if (creature != null)
            {
                casters.Add(creature);
                continue;
            }

            MainFile.Logger.Warn(
                $"Skipped restoring dampen caster during multiplayer combat snapshot restore for {restoreContext} because creature {DescribeCreatureReferenceForLog(reference)} was not found.");
        }
    }

    private static bool TryGetTrackedCardIntMapFieldName(PowerModel power, out string fieldName)
    {
        fieldName = power switch
        {
            AfterimagePower => "amountsForPlayedCards",
            CalamityPower => "amountsForPlayedCards",
            GravityPower => "amountsForPlayedCards",
            OblivionPower => "amountsForPlayedCards",
            MonologuePower => "amountsForPlayedCards",
            SerpentFormPower => "amountsForPlayedCards",
            StormPower => "amountsForPlayedCards",
            StranglePower => "amountsForPlayedCards",
            SubroutinePower => "amountsForPlayedCards",
            RupturePower => "playedCards",
            DampenPower => "downgradedCardsToOldUpgradeLevels",
            _ => string.Empty
        };

        return fieldName.Length > 0;
    }

    private static void RestoreFabricatorState(MonsterModel monster, CombatCreatureSnapshot snapshot, string restoreContext)
    {
        if (monster is not Fabricator || snapshot.FabricatorLastSpawnedMonsterId == null)
            return;

        try
        {
            var lastSpawnedMonster = ModelDb.GetById<MonsterModel>(snapshot.FabricatorLastSpawnedMonsterId);
            TrySetFieldValueByPath(monster, "_lastSpawned", lastSpawnedMonster);
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn(
                $"Failed to restore fabricator last-spawned state during multiplayer combat snapshot restore for {restoreContext}: {e.Message}");
        }
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

            foreach (var (_, restoredPower) in MatchPowerSnapshotsToRestoredPowers(creature, snapshot.Powers, restoreContext))
            {
                await TrySynchronizeSpecialPowerStateAsync(restoredPower, restoreContext);
            }

            SynchronizeThievingHopperStolenCardDisplay(creature, restoreContext);
        }

        RebuildInterceptPowerCoverageState(combatState, restoreContext);
    }

    private static IEnumerable<(CombatPowerSnapshot Snapshot, PowerModel Power)> MatchPowerSnapshotsToRestoredPowers(
        Creature creature,
        IReadOnlyList<CombatPowerSnapshot> powerSnapshots,
        string restoreContext)
    {
        var powersById = creature.Powers
            .GroupBy(power => power.Id)
            .ToDictionary(group => group.Key, group => new Queue<PowerModel>(group));

        foreach (var powerSnapshot in powerSnapshots)
        {
            if (!powersById.TryGetValue(powerSnapshot.Id, out var matchingPowers) || matchingPowers.Count == 0)
            {
                MainFile.Logger.Warn(
                    $"Skipped restoring multiplayer power snapshot {powerSnapshot.Id} during {restoreContext} " +
                    $"because no unmatched restored power instance remained for creature " +
                    $"{creature.Monster?.Id.Entry ?? creature.Player?.NetId.ToString() ?? "unknown"}.");
                continue;
            }

            yield return (powerSnapshot, matchingPowers.Dequeue());
        }
    }

    private static async Task TrySynchronizeSpecialPowerStateAsync(PowerModel restoredPower, string restoreContext)
    {
        if (restoredPower is SurroundedPower surroundedPower)
        {
            await SynchronizeSurroundedPowerStateAsync(surroundedPower, restoreContext);
            return;
        }

        if (restoredPower is not SandpitPower sandpitPower)
            return;

        if (sandpitPower.Target?.Player == null || SandpitPowerUpdateCreaturePositionsMethod == null)
            return;

        try
        {
            if (SandpitPowerUpdateCreaturePositionsMethod.Invoke(sandpitPower, null) is Task updateTask)
                await updateTask;
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn(
                $"Failed to synchronize sandpit power during multiplayer combat snapshot restore for {restoreContext}. " +
                $"Amount={sandpitPower.Amount} TargetPlayer={sandpitPower.Target.Player.NetId}: {e.Message}");
        }
    }

    private static async Task SynchronizeSurroundedPowerStateAsync(SurroundedPower surroundedPower, string restoreContext)
    {
        if (SurroundedPowerFaceDirectionMethod == null)
            return;

        try
        {
            if (SurroundedPowerFaceDirectionMethod.Invoke(surroundedPower, [surroundedPower.Facing]) is Task faceTask)
                await faceTask;

            await RefreshEnemyIntentDisplaysAsync(surroundedPower.Owner?.CombatState);
        }
        catch (Exception e)
        {
            var ownerPlayerId = surroundedPower.Owner?.Player is Player ownerPlayer ? ownerPlayer.NetId.ToString() : "unknown";
            MainFile.Logger.Warn(
                $"Failed to synchronize surrounded power during multiplayer combat snapshot restore for {restoreContext}. " +
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

    private static async Task TryRefreshEnemyIntentDisplaysAsync(CombatState? combatState, string restoreContext)
    {
        try
        {
            await RefreshEnemyIntentDisplaysAsync(combatState);
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"Failed to refresh multiplayer enemy intents for {restoreContext}: {e.Message}");
        }
    }

    private static void RebuildInterceptPowerCoverageState(CombatState combatState, string restoreContext)
    {
        var restoredCoverageCount = 0;

        foreach (var coveredPower in combatState.Creatures
                     .SelectMany(creature => creature.Powers.OfType<CoveredPower>()))
        {
            var applier = coveredPower.Applier;
            var protectedCreature = coveredPower.Owner;
            if (applier == null || protectedCreature == null)
            {
                MainFile.Logger.Warn(
                    $"Skipped rebuilding intercept coverage during multiplayer combat snapshot restore for {restoreContext} " +
                    $"because covered power {coveredPower.Id} was missing owner or applier.");
                continue;
            }

            var interceptPower = applier.GetPower<InterceptPower>();
            if (interceptPower == null)
            {
                MainFile.Logger.Warn(
                    $"Skipped rebuilding intercept coverage during multiplayer combat snapshot restore for {restoreContext} " +
                    $"because creature {DescribeCreatureForLog(applier)} had no intercept power.");
                continue;
            }

            interceptPower.AddCoveredCreature(protectedCreature);
            restoredCoverageCount++;
        }

        if (restoredCoverageCount > 0)
        {
            MainFile.Logger.Info(
                $"Rebuilt {restoredCoverageCount} covered-creature link(s) for intercept powers during multiplayer combat snapshot restore for {restoreContext}.");
        }
    }

    private static void RestoreHiddenLiveAllyVisuals(CombatState combatState, string restoreContext)
    {
        var combatRoom = NCombatRoom.Instance;
        if (combatRoom == null)
            return;

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
        }
    }

    private static void RepositionPlayersAndPetsAfterRestore(CombatRoom combatRoom, CombatState combatState, string restoreContext)
    {
        var combatRoomNode = NCombatRoom.Instance;
        var encounter = combatRoom.Encounter;
        if (combatRoomNode == null || encounter == null)
            return;

        var allyNodes = combatRoomNode.CreatureNodes
            .Where(node => node.Entity != null && combatState.Allies.Contains(node.Entity))
            .ToList();
        if (!allyNodes.Any(node => node.Entity.PetOwner != null))
            return;

        try
        {
            NCombatRoom.PositionPlayersAndPets(allyNodes, encounter.GetCameraScaling(), encounter.FullyCenterPlayers);
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn(
                $"Failed to reposition multiplayer players/pets after restore for {restoreContext}: {e.Message}");
        }
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

        var localStolenCards = creature.Powers
            .OfType<SwipePower>()
            .Select(power => power.StolenCard)
            .Where(card => card != null && LocalContext.IsMine(card))
            .Cast<CardModel>()
            .ToList();
        for (var index = 0; index < localStolenCards.Count; index++)
        {
            var stolenCardNode = NCard.Create(localStolenCards[index], ModelVisibility.Visible);
            if (stolenCardNode == null)
                continue;

            stolenCardMarker.AddChild(stolenCardNode);
            stolenCardNode.Position += stolenCardNode.Size * 0.5f + Vector2.Right * (18f * index);
            stolenCardNode.UpdateVisuals(PileType.Deck, CardPreviewMode.Normal);
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
            && MultiplayerSerializableCardStateService.MatchesDeckCardForRestore(deckCard.ToSerializable(), desiredDeckCard));
        if (matchingDeckCard != null)
        {
            usedDeckCards.Add(matchingDeckCard);
            return matchingDeckCard;
        }

        try
        {
            var restoredDeckCard = MultiplayerSerializableCardStateService.ResolveCardForRestore(
                desiredDeckCard,
                player);
            var recreatedDeckCard = runState.LoadCard(restoredDeckCard, player);
            player.Deck.AddInternal(recreatedDeckCard, -1, false);
            usedDeckCards.Add(recreatedDeckCard);
            return recreatedDeckCard;
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn(
                $"Failed to recreate missing deck version for multiplayer combat card snapshot restore. " +
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
            var deckVersion = ResolveRemovedDeckVersion(runState, player, cardSnapshot, usedDeckCards);
            var loadableCard = MultiplayerSerializableCardStateService.ResolveCardForRestore(
                cardSnapshot.Card,
                player,
                deckVersion?.ToSerializable() ?? cardSnapshot.DeckVersionCard);
            var card = runState.LoadCard(loadableCard, player);
            RegisterCardInCombatState(card);
            card.DeckVersion = deckVersion;
            ApplyCardSnapshotState(card, cardSnapshot);
            card.RemoveFromState();
            return card;
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn(
                $"Failed to restore detached combat card for multiplayer combat snapshot restore for {restoreContext}. " +
                $"Player={player.NetId}, Card={cardSnapshot.Card.Id}: {e.Message}");
            return null;
        }
    }

    private static CardModel? ResolveRemovedDeckVersion(
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
            && MultiplayerSerializableCardStateService.MatchesDeckCardForRestore(deckCard.ToSerializable(), desiredDeckCard));

        CardModel deckVersion;
        if (matchingDeckCard != null)
        {
            deckVersion = matchingDeckCard;
        }
        else
        {
            var restoredDeckCard = MultiplayerSerializableCardStateService.ResolveCardForRestore(
                desiredDeckCard,
                player);
            deckVersion = runState.LoadCard(restoredDeckCard, player);
        }

        usedDeckCards.Add(deckVersion);
        deckVersion.RemoveFromState();
        return deckVersion;
    }

    private static bool AreSerializableCardsEquivalent(SerializableCard left, SerializableCard right)
    {
        return JsonSerializer.Serialize(left, JsonOptions) == JsonSerializer.Serialize(right, JsonOptions);
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

    private static CombatLiveCardReferenceSnapshot? CaptureLiveCardReference(CardModel? card)
    {
        if (card?.Owner == null)
            return null;

        var pile = card.Pile;
        if (pile == null)
            return null;

        var cardIndex = -1;
        for (var index = 0; index < pile.Cards.Count; index++)
        {
            if (!ReferenceEquals(pile.Cards[index], card))
                continue;

            cardIndex = index;
            break;
        }

        if (cardIndex < 0)
            return null;

        return new CombatLiveCardReferenceSnapshot
        {
            PlayerId = card.Owner.NetId,
            PileType = pile.Type,
            CardIndex = cardIndex,
            Card = CombatCardSnapshot.FromCard(card)
        };
    }

    private static CardModel? ResolveLiveCardReference(CombatState combatState, CombatLiveCardReferenceSnapshot? reference)
    {
        if (reference == null)
            return null;

        var player = combatState.RunState?.GetPlayer(reference.PlayerId);
        if (player == null)
            return null;

        var pile = CardPile.Get(reference.PileType, player);
        if (pile != null)
        {
            if (reference.CardIndex >= 0 && reference.CardIndex < pile.Cards.Count)
            {
                var indexedCard = pile.Cards[reference.CardIndex];
                if (DoesCardMatchReference(indexedCard, reference))
                    return indexedCard;
            }

            var matchingCard = pile.Cards.FirstOrDefault(card => DoesCardMatchReference(card, reference));
            if (matchingCard != null)
                return matchingCard;
        }

        return player.PlayerCombatState?.AllPiles
            .SelectMany(currentPile => currentPile.Cards)
            .FirstOrDefault(card => DoesCardMatchReference(card, reference));
    }

    private static bool DoesCardMatchReference(CardModel card, CombatLiveCardReferenceSnapshot reference)
    {
        if (reference.Card == null)
            return true;

        var currentCard = card.ToSerializable();
        if (!AreSerializableCardsEquivalent(currentCard, reference.Card.Card)
            && !MultiplayerSerializableCardStateService.MatchesDeckCardForRestore(currentCard, reference.Card.Card))
            return false;

        var currentAfflictionId = card.Affliction?.Id;
        if (currentAfflictionId != reference.Card.Affliction)
            return false;

        var currentAfflictionCount = card.Affliction?.Amount ?? 0;
        if (currentAfflictionCount != reference.Card.AfflictionCount)
            return false;

        if (reference.Card.Keywords == null)
            return true;

        return card.Keywords.SequenceEqual(reference.Card.Keywords);
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
            TryRestoreBooleanMember(combatManager, CombatManagerIsEnemyTurnStartedProperty, CombatManagerIsEnemyTurnStartedField, snapshot.IsEnemyTurnStarted, "combat manager IsEnemyTurnStarted");
            TryRestoreBooleanMember(combatManager, CombatManagerEndingPlayerTurnPhaseOneProperty, CombatManagerEndingPlayerTurnPhaseOneField, snapshot.EndingPlayerTurnPhaseOne, "combat manager EndingPlayerTurnPhaseOne");
            TryRestoreBooleanMember(combatManager, CombatManagerEndingPlayerTurnPhaseTwoProperty, CombatManagerEndingPlayerTurnPhaseTwoField, snapshot.EndingPlayerTurnPhaseTwo, "combat manager EndingPlayerTurnPhaseTwo");
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"Failed to restore multiplayer combat manager runtime state for {restoreContext}: {e.Message}");
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

        foreach (var snapshot in snapshots.Where(snapshot => string.Equals(snapshot.MonsterId?.Entry, monsterId, StringComparison.Ordinal)))
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
                    $"{restoreContext}/tough-egg-node-resync");
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
                $"Failed to recreate missing combat room creature node during multiplayer restore. " +
                $"Monster={snapshot.MonsterId?.Entry ?? "unknown"}, Slot={snapshot.SlotName ?? "unknown"}: {e.Message}");
            return new EnsuredCreatureNodeResult(combatRoomNode.GetCreatureNode(creature), false);
        }
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
        }
    }

    private static void RemoveCreaturesMissingFromSnapshot(
        CombatState combatState,
        CombatStateSnapshot snapshot,
        string restoreContext,
        SnapshotResolutionContext resolutionContext)
    {
        var snapshotEnemyCount = snapshot.Creatures.Count(creature => creature.PlayerId == null);
        var currentEnemyCreatures = combatState.Creatures.Where(creature => creature.Player == null).ToList();
        if (snapshotEnemyCount >= currentEnemyCreatures.Count)
            return;

        var matchedEnemies = ResolveMatchedSnapshotEnemies(combatState, snapshot.Creatures, resolutionContext);
        var matchedEnemySet = matchedEnemies.ToHashSet();
        var extraCreatures = currentEnemyCreatures.Where(creature => !matchedEnemySet.Contains(creature)).ToList();
        if (extraCreatures.Count == 0)
            return;

        foreach (var creature in extraCreatures)
            RemoveCreatureFromCombatState(combatState, creature, restoreContext);

        if (snapshot.EncounterSlots.Count > 0)
            combatState.SortEnemiesBySlotName();
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
                $"Failed to fully remove creature during multiplayer combat snapshot restore for {restoreContext}. " +
                $"Monster={creature.Monster?.Id.Entry ?? "unknown"}, Slot={creature.SlotName ?? "unknown"}: {e.Message}");
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

        var byPetOwner = FindPetCandidates(
            combatState,
            snapshot.MonsterId,
            snapshot.PetOwnerPlayerId,
            excludedCreatures);
        if (byPetOwner.Count > 0)
        {
            if (!string.IsNullOrWhiteSpace(snapshot.SlotName))
            {
                var byPetSlot = byPetOwner.FirstOrDefault(creature =>
                    string.Equals(creature.SlotName, snapshot.SlotName, StringComparison.Ordinal));
                if (byPetSlot != null)
                    return byPetSlot;
            }

            return snapshot.MonsterInstanceIndex >= 0 && snapshot.MonsterInstanceIndex < byPetOwner.Count
                ? byPetOwner[snapshot.MonsterInstanceIndex]
                : byPetOwner[0];
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
            .Where(creature => creature.Player == null && creature.Monster?.Id == snapshot.MonsterId)
            .Where(creature => excludedCreatures == null || !excludedCreatures.Contains(creature))
            .ToList();
        if (byMonsterId.Count == 0)
            return null;

        return snapshot.MonsterInstanceIndex >= 0 && snapshot.MonsterInstanceIndex < byMonsterId.Count
            ? byMonsterId[snapshot.MonsterInstanceIndex]
            : byMonsterId[0];
    }

    private static Creature? ResolveCreatureReference(CombatState combatState, CombatCreatureReferenceSnapshot reference)
    {
        if (reference.PlayerId.HasValue)
        {
            return combatState.Creatures.FirstOrDefault(creature =>
                creature.Player?.NetId == reference.PlayerId.Value);
        }

        if (reference.CombatId.HasValue)
        {
            var byCombatId = combatState.Creatures.FirstOrDefault(creature =>
                creature.Player == null
                && creature.CombatId == reference.CombatId.Value);
            if (byCombatId != null)
                return byCombatId;
        }

        var byPetOwner = FindPetCandidates(combatState, reference.MonsterId, reference.PetOwnerPlayerId);
        if (byPetOwner.Count > 0)
        {
            if (!string.IsNullOrWhiteSpace(reference.SlotName))
            {
                var byPetSlot = byPetOwner.FirstOrDefault(creature =>
                    string.Equals(creature.SlotName, reference.SlotName, StringComparison.Ordinal));
                if (byPetSlot != null)
                    return byPetSlot;
            }

            return reference.MonsterInstanceIndex >= 0 && reference.MonsterInstanceIndex < byPetOwner.Count
                ? byPetOwner[reference.MonsterInstanceIndex]
                : byPetOwner[0];
        }

        if (!string.IsNullOrWhiteSpace(reference.SlotName))
        {
            var bySlot = combatState.Creatures.FirstOrDefault(creature =>
                creature.Player == null
                && string.Equals(creature.SlotName, reference.SlotName, StringComparison.Ordinal));
            if (bySlot != null)
                return bySlot;
        }

        var byMonsterId = combatState.Creatures
            .Where(creature => creature.Player == null && creature.Monster?.Id == reference.MonsterId)
            .ToList();
        if (byMonsterId.Count == 0)
            return null;

        return reference.MonsterInstanceIndex >= 0 && reference.MonsterInstanceIndex < byMonsterId.Count
            ? byMonsterId[reference.MonsterInstanceIndex]
            : byMonsterId[0];
    }

    private static List<Creature> FindPetCandidates(
        CombatState combatState,
        ModelId? monsterId,
        ulong? petOwnerPlayerId,
        ISet<Creature>? excludedCreatures = null)
    {
        if (monsterId == null
            || !TryResolvePetOwnerPlayer(combatState, monsterId, petOwnerPlayerId, out var petOwner)
            || petOwner == null)
        {
            return [];
        }

        return combatState.Creatures
            .Where(creature => creature.Player == null
                && creature.PetOwner?.NetId == petOwner.NetId
                && creature.Monster?.Id == monsterId)
            .Where(creature => excludedCreatures == null || !excludedCreatures.Contains(creature))
            .ToList();
    }

    private static bool TryResolvePetOwnerPlayer(
        CombatState combatState,
        ModelId? monsterId,
        ulong? petOwnerPlayerId,
        out Player? petOwner)
    {
        petOwner = null;
        if (petOwnerPlayerId.HasValue)
        {
            petOwner = combatState.GetPlayer(petOwnerPlayerId.Value);
            return petOwner != null;
        }

        if (monsterId == null)
            return false;

        var distinctOwners = combatState.Creatures
            .Where(creature => creature.Monster?.Id == monsterId && creature.PetOwner != null)
            .Select(creature => creature.PetOwner!)
            .GroupBy(owner => owner.NetId)
            .Select(group => group.First())
            .ToList();
        if (distinctOwners.Count == 1)
        {
            petOwner = distinctOwners[0];
            return true;
        }

        return TryResolveKnownPetOwnerPlayer(combatState, monsterId, out petOwner);
    }

    private static bool TryResolveKnownPetOwnerPlayer(CombatState? combatState, ModelId? monsterId, out Player? petOwner)
    {
        petOwner = null;
        if (combatState == null
            || monsterId == null
            || !KnownPetOwnerCharacterIdsByMonsterId.TryGetValue(monsterId.Entry, out var ownerCharacterId))
        {
            return false;
        }

        var matchingPlayers = combatState.Players
            .Where(player => string.Equals(player.Character.Id.Entry, ownerCharacterId, StringComparison.Ordinal))
            .ToList();
        if (matchingPlayers.Count != 1)
            return false;

        petOwner = matchingPlayers[0];
        return true;
    }

    private static ulong? ResolveCapturedPetOwnerPlayerId(Creature? creature, ModelId? monsterId)
    {
        if (creature?.PetOwner != null)
            return creature.PetOwner.NetId;

        if (!TryResolveKnownPetOwnerPlayer(creature?.CombatState, monsterId, out var petOwner) || petOwner == null)
            return null;

        var creatureLabel = creature == null
            ? monsterId?.Entry ?? "unknown"
            : DescribeCreatureForLog(creature);
        MainFile.Logger.Warn(
            $"Inferred missing multiplayer pet owner {petOwner.NetId} while capturing snapshot creature " +
            $"{creatureLabel}.");
        return petOwner.NetId;
    }

    private static int GetMonsterInstanceIndex(Creature creature)
    {
        if (creature.Player != null || creature.Monster?.Id == null || creature.CombatState == null)
            return 0;

        var matchingCreatures = creature.CombatState.Creatures
            .Where(entry => entry.Player == null && entry.Monster?.Id == creature.Monster.Id)
            .ToList();
        var index = matchingCreatures.IndexOf(creature);
        return index >= 0 ? index : 0;
    }

    private static int? CaptureTenderCardsPlayedThisTurn(TenderPower tenderPower)
    {
        try
        {
            if (TenderPowerCardsPlayedThisTurnProperty?.GetValue(tenderPower) is int propertyValue)
                return propertyValue;

            if (TenderPowerCardsPlayedThisTurnField?.GetValue(tenderPower) is int fieldValue)
                return fieldValue;
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"Failed to capture cards-played count for tender power: {e.Message}");
        }

        return null;
    }

    private static List<SpecialFieldSnapshot> CaptureSpecialMonsterFields(MonsterModel monster)
    {
        if (!MultiplayerPreviousStepSpecialSnapshotRegistry.TryGetMonsterPrivateFields(monster.Id.Entry, out var fieldNames))
            return [];

        return CaptureFields(monster, fieldNames);
    }

    private static List<SpecialFieldSnapshot> CaptureFields(object target, IReadOnlyList<string> fieldNames)
    {
        var snapshots = new List<SpecialFieldSnapshot>();
        foreach (var fieldName in fieldNames)
        {
            if (!TryResolveFieldPath(target, fieldName, out var fieldOwner, out var field))
                continue;

            var value = field.GetValue(fieldOwner);
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
        var snapshot = fieldSnapshots.FirstOrDefault(field => string.Equals(field.FieldName, fieldName, StringComparison.Ordinal));
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
        var snapshot = fieldSnapshots.FirstOrDefault(field => string.Equals(field.FieldName, fieldName, StringComparison.Ordinal));
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
        if (!TryResolveFieldPath(target, fieldSnapshot.FieldName, out var fieldOwner, out var field))
            return;

        try
        {
            var value = JsonSerializer.Deserialize(fieldSnapshot.ValueJson, field.FieldType);
            field.SetValue(fieldOwner, value);
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"Failed to restore {logContext} field {fieldSnapshot.FieldName}: {e.Message}");
        }
    }

    private static bool TrySetFieldValueByPath(object target, string fieldPath, object? value)
    {
        if (!TryResolveFieldPath(target, fieldPath, out var fieldOwner, out var field))
            return false;

        field.SetValue(fieldOwner, value);
        return true;
    }

    private static bool TryResolveFieldPath(object target, string fieldPath, out object fieldOwner, out FieldInfo field)
    {
        fieldOwner = target;
        field = null!;

        if (string.IsNullOrWhiteSpace(fieldPath))
            return false;

        var currentOwner = target;
        var segments = fieldPath.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
            return false;

        for (var index = 0; index < segments.Length; index++)
        {
            var segment = segments[index];
            var currentField = FindInstanceField(currentOwner.GetType(), segment);
            if (currentField == null)
                return false;

            if (index == segments.Length - 1)
            {
                fieldOwner = currentOwner;
                field = currentField;
                return true;
            }

            currentOwner = currentField.GetValue(currentOwner)!;
            if (currentOwner == null)
                return false;
        }

        return false;
    }

    private static FieldInfo? FindInstanceField(Type type, string fieldName)
    {
        for (var currentType = type; currentType != null; currentType = currentType.BaseType)
        {
            var field = currentType.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);
            if (field != null)
                return field;
        }

        return null;
    }

    private static object? GetPowerInternalData(PowerModel power)
    {
        try
        {
            return PowerModelInternalDataField?.GetValue(power);
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"Failed to read power internal data for {power.Id}: {e.Message}");
            return null;
        }
    }

    private static bool TryGetPowerInternalDataFieldValue<T>(PowerModel power, string fieldName, out T? value)
    {
        value = default;
        var internalData = GetPowerInternalData(power);
        if (internalData == null)
            return false;

        var field = FindInstanceField(internalData.GetType(), fieldName);
        if (field?.GetValue(internalData) is not T typedValue)
            return false;

        value = typedValue;
        return true;
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

    private static List<DynamicVarSnapshot> CaptureDynamicVars(DynamicVarSet dynamicVars)
    {
        var snapshots = new List<DynamicVarSnapshot>();
        foreach (var (name, dynamicVar) in dynamicVars)
        {
            snapshots.Add(new DynamicVarSnapshot
            {
                Name = name,
                BaseValue = dynamicVar.BaseValue,
                HasStringValue = dynamicVar is StringVar,
                StringValue = dynamicVar is StringVar stringVar ? stringVar.StringValue : null
            });
        }

        return snapshots;
    }

    private static void CaptureComplexPowerState(CombatPowerSnapshot snapshot, PowerModel actualPower)
    {
        if (actualPower is NightmarePower
            && TryGetPowerInternalDataFieldValue<CardModel>(actualPower, "selectedCard", out var selectedCard)
            && selectedCard != null)
        {
            snapshot.NightmareSelectedCard = CombatCardSnapshot.FromCard(selectedCard);
        }

        if (actualPower is VitalSparkPower
            && TryGetPowerInternalDataFieldValue<ISet<Player>>(actualPower, "playersTriggeredThisTurn", out var triggeredPlayers)
            && triggeredPlayers is { Count: > 0 })
        {
            snapshot.VitalSparkTriggeredPlayerIds = triggeredPlayers.Select(player => player.NetId).ToList();
        }

        var creatureDecimalFieldName = actualPower switch
        {
            PossessStrengthPower => "stolenStrength",
            PossessSpeedPower => "stolenDexterity",
            _ => null
        };
        if (creatureDecimalFieldName != null
            && TryGetPowerInternalDataFieldValue<IDictionary<Creature, decimal>>(actualPower, creatureDecimalFieldName, out var creatureDecimalEntries)
            && creatureDecimalEntries is { Count: > 0 })
        {
            snapshot.CreatureDecimalEntries = CaptureCreatureDecimalEntries(creatureDecimalEntries);
        }

        if (TryGetTrackedCardIntMapFieldName(actualPower, out var cardMapFieldName)
            && TryGetPowerInternalDataFieldValue<IDictionary<CardModel, int>>(actualPower, cardMapFieldName, out var cardIntEntries)
            && cardIntEntries is { Count: > 0 })
        {
            snapshot.CardIntEntries = CaptureLiveCardValueEntries(cardIntEntries);
        }

        if (actualPower is DampenPower
            && TryGetPowerInternalDataFieldValue<ISet<Creature>>(actualPower, "casters", out var dampenCasters)
            && dampenCasters is { Count: > 0 })
        {
            snapshot.DampenCasterReferences = dampenCasters
                .Select(CombatCreatureReferenceSnapshot.FromCreature)
                .Where(reference => reference != null)
                .Cast<CombatCreatureReferenceSnapshot>()
                .ToList();
        }
    }

    private static List<CombatCreatureDecimalSnapshot> CaptureCreatureDecimalEntries(IDictionary<Creature, decimal> entries)
    {
        var snapshots = new List<CombatCreatureDecimalSnapshot>();
        foreach (var (creature, value) in entries)
        {
            var reference = CombatCreatureReferenceSnapshot.FromCreature(creature);
            if (reference == null)
                continue;

            snapshots.Add(new CombatCreatureDecimalSnapshot
            {
                Creature = reference,
                Value = value
            });
        }

        return snapshots;
    }

    private static List<CombatLiveCardValueSnapshot> CaptureLiveCardValueEntries(IDictionary<CardModel, int> entries)
    {
        var snapshots = new List<CombatLiveCardValueSnapshot>();
        foreach (var (card, value) in entries)
        {
            var reference = CaptureLiveCardReference(card);
            if (reference == null)
                continue;

            snapshots.Add(new CombatLiveCardValueSnapshot
            {
                Card = reference,
                Value = value
            });
        }

        return snapshots;
    }

    private static ModelId? CaptureLastSpawnedMonsterId(Fabricator fabricator)
    {
        if (!TryResolveFieldPath(fabricator, "_lastSpawned", out var fieldOwner, out var field))
            return null;

        return field.GetValue(fieldOwner) is MonsterModel lastSpawnedMonster ? lastSpawnedMonster.Id : null;
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

    public sealed partial class CombatStateSnapshot
    {
        public List<string> EncounterSlots { get; set; } = [];
        public CombatManagerSnapshot? CombatManager { get; set; }

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

    public sealed partial class CombatCreatureSnapshot
    {
        public int MonsterInstanceIndex { get; set; }
        public uint? CombatId { get; set; }
        public string? SlotName { get; set; }
        public ulong? PetOwnerPlayerId { get; set; }
        public string? CurrentMoveFollowUpStateId { get; set; }
        public bool IsTemporaryStunned { get; set; }
        public string? CurrentMoveStateId { get; set; }
        public List<string> MoveStateLogIds { get; set; } = [];
        public bool PerformedFirstMove { get; set; }
        public List<SpecialFieldSnapshot> SpecialFields { get; set; } = [];
        public CombatVector2Snapshot? NodeGlobalPosition { get; set; }
        public ModelId? FabricatorLastSpawnedMonsterId { get; set; }

        public static CombatCreatureSnapshot FromNetState(NetFullCombatState.CreatureState state, Creature? creature, int monsterInstanceIndex)
        {
            var moveStateMachine = creature?.Monster?.MoveStateMachine;
            var creatureNode = creature != null ? NCombatRoom.Instance?.GetCreatureNode(creature) : null;
            return new CombatCreatureSnapshot
            {
                MonsterId = state.monsterId,
                MonsterInstanceIndex = monsterInstanceIndex,
                CombatId = creature?.CombatId,
                SlotName = creature?.SlotName,
                PetOwnerPlayerId = ResolveCapturedPetOwnerPlayerId(creature, state.monsterId),
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
                    : null,
                FabricatorLastSpawnedMonsterId = creature?.Monster is Fabricator fabricator
                    ? CaptureLastSpawnedMonsterId(fabricator)
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

    public sealed partial class CombatPowerSnapshot
    {
        public CombatCreatureReferenceSnapshot? Applier { get; set; }
        public ulong? TargetPlayerId { get; set; }
        public int? ThieveryStolenGold { get; set; }
        public int? TenderCardsPlayedThisTurn { get; set; }
        public CombatCardSnapshot? SwipeStolenCard { get; set; }
        public List<DynamicVarSnapshot> DynamicVars { get; set; } = [];
        public CombatCardSnapshot? NightmareSelectedCard { get; set; }
        public List<ulong>? VitalSparkTriggeredPlayerIds { get; set; }
        public List<CombatCreatureDecimalSnapshot>? CreatureDecimalEntries { get; set; }
        public List<CombatLiveCardValueSnapshot>? CardIntEntries { get; set; }
        public List<CombatCreatureReferenceSnapshot>? DampenCasterReferences { get; set; }
        public List<SpecialFieldSnapshot> SpecialFields { get; set; } = [];

        public static CombatPowerSnapshot FromNetState(NetFullCombatState.PowerState state, PowerModel? actualPower)
        {
            var snapshot = new CombatPowerSnapshot
            {
                Id = state.id,
                Amount = state.amount,
                Applier = CombatCreatureReferenceSnapshot.FromCreature(actualPower?.Applier),
                DynamicVars = actualPower != null ? CaptureDynamicVars(actualPower.DynamicVars) : []
            };

            if (actualPower is ThieveryPower thieveryPower)
            {
                snapshot.TargetPlayerId = thieveryPower.Target?.Player?.NetId;
                snapshot.ThieveryStolenGold = thieveryPower.DynamicVars.Gold.IntValue;
            }
            else if (actualPower is HeistPower heistPower)
            {
                snapshot.TargetPlayerId = heistPower.Target?.Player?.NetId;
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

            if (actualPower is TenderPower tenderPower)
                snapshot.TenderCardsPlayedThisTurn = CaptureTenderCardsPlayedThisTurn(tenderPower);

            if (actualPower != null)
                CaptureComplexPowerState(snapshot, actualPower);

            if (actualPower != null
                && MultiplayerPreviousStepSpecialSnapshotRegistry.TryGetPowerPrivateFields(actualPower.Id.Entry, out var powerFieldNames))
            {
                snapshot.SpecialFields = CaptureFields(actualPower, powerFieldNames);
            }

            return snapshot;
        }
    }

    public sealed class CombatCreatureReferenceSnapshot
    {
        public ulong? PlayerId { get; set; }
        public uint? CombatId { get; set; }
        public string? SlotName { get; set; }
        public ulong? PetOwnerPlayerId { get; set; }
        public ModelId? MonsterId { get; set; }
        public int MonsterInstanceIndex { get; set; }

        public static CombatCreatureReferenceSnapshot? FromCreature(Creature? creature)
        {
            if (creature == null)
                return null;

            return new CombatCreatureReferenceSnapshot
            {
                PlayerId = creature.Player?.NetId,
                CombatId = creature.Player == null ? creature.CombatId : null,
                SlotName = creature.Player == null ? creature.SlotName : null,
                PetOwnerPlayerId = creature.PetOwner?.NetId,
                MonsterId = creature.Monster?.Id,
                MonsterInstanceIndex = GetMonsterInstanceIndex(creature)
            };
        }
    }

    public sealed partial class CombatPlayerSnapshot
    {
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
                        actualPlayer))
                    .ToList(),
                Potions = state.potions.Select(CombatPotionSnapshot.FromNetState).ToList(),
                Relics = state.relics.Select(CombatRelicSnapshot.FromNetState).ToList(),
                Orbs = state.orbs.Select(CombatOrbSnapshot.FromNetState).ToList(),
                RngSet = state.rngSet,
                OddsSet = ResolveOddsSet(state.playerId),
                RelicGrabBag = state.relicGrabBag
            };
        }
    }

    public sealed partial class CombatPileSnapshot
    {
        public static CombatPileSnapshot FromNetState(NetFullCombatState.CombatPileState state, Player? actualPlayer)
        {
            var actualPile = actualPlayer != null ? CardPile.Get(state.pileType, actualPlayer) : null;
            var cards = new List<CombatCardSnapshot>();
            for (var index = 0; index < state.cards.Count; index++)
            {
                var actualCard = actualPile != null && index < actualPile.Cards.Count
                    ? actualPile.Cards[index]
                    : null;
                cards.Add(CombatCardSnapshot.FromNetState(state.cards[index], actualCard, actualPlayer));
            }

            return new CombatPileSnapshot
            {
                PileType = state.pileType,
                Cards = cards
            };
        }
    }

    public sealed partial class CombatCardSnapshot
    {
        public bool HadDeckVersion { get; set; }
        public SerializableCard? DeckVersionCard { get; set; }

        public static CombatCardSnapshot FromNetState(NetFullCombatState.CardState state, CardModel? actualCard, Player? actualPlayer)
        {
            return new CombatCardSnapshot
            {
                Card = MultiplayerSerializableCardStateService.CaptureCombatCard(state.card, actualCard, actualPlayer),
                HadDeckVersion = actualCard?.DeckVersion != null,
                DeckVersionCard = MultiplayerSerializableCardStateService.CaptureLiveCard(actualCard?.DeckVersion),
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
                Card = MultiplayerSerializableCardStateService.CaptureLiveCard(card)!,
                HadDeckVersion = card.DeckVersion != null,
                DeckVersionCard = MultiplayerSerializableCardStateService.CaptureLiveCard(card.DeckVersion),
                Affliction = card.Affliction?.Id,
                AfflictionCount = card.Affliction?.Amount ?? 0,
                Keywords = card.Keywords.ToList()
            };
        }
    }

    public sealed class DynamicVarSnapshot
    {
        public string Name { get; set; } = string.Empty;
        public decimal BaseValue { get; set; }
        public bool HasStringValue { get; set; }
        public string? StringValue { get; set; }
    }

    public sealed class CombatLiveCardReferenceSnapshot
    {
        public ulong PlayerId { get; set; }
        public PileType PileType { get; set; }
        public int CardIndex { get; set; }
        public CombatCardSnapshot? Card { get; set; }
    }

    public sealed class CombatLiveCardValueSnapshot
    {
        public CombatLiveCardReferenceSnapshot? Card { get; set; }
        public int Value { get; set; }
    }

    public sealed class CombatCreatureDecimalSnapshot
    {
        public CombatCreatureReferenceSnapshot? Creature { get; set; }
        public decimal Value { get; set; }
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
                .Where(entry => string.Equals(entry.Key.MonsterId?.Entry, monsterId, StringComparison.Ordinal))
                .Select(entry => new ResolvedCreatureSnapshot(entry.Key, entry.Value))
                .ToList();
        }
    }

    private readonly record struct EnsuredCreatureNodeResult(NCreature? Node, bool WasRecreated);
    private readonly record struct ResolvedCreatureSnapshot(CombatCreatureSnapshot Snapshot, Creature Creature);
    private readonly record struct ReadyMultiplayerCombatRestoreContext(CombatRoom CombatRoom, CombatState CombatState);

    private static string DescribeSnapshotCreatureForLog(CombatCreatureSnapshot snapshot)
    {
        var monsterId = snapshot.MonsterId?.Entry ?? "unknown";
        if (snapshot.PetOwnerPlayerId.HasValue)
            return $"{monsterId}:pet:{snapshot.PetOwnerPlayerId.Value}:index:{snapshot.MonsterInstanceIndex}";

        return string.IsNullOrWhiteSpace(snapshot.SlotName) ? monsterId : $"{monsterId}:{snapshot.SlotName}";
    }

    private static string DescribeCreatureReferenceForLog(CombatCreatureReferenceSnapshot reference)
    {
        if (reference.PlayerId.HasValue)
            return $"player:{reference.PlayerId.Value}";

        var monsterId = reference.MonsterId?.Entry ?? "unknown";
        if (reference.PetOwnerPlayerId.HasValue)
            return $"{monsterId}:pet:{reference.PetOwnerPlayerId.Value}:index:{reference.MonsterInstanceIndex}";

        if (!string.IsNullOrWhiteSpace(reference.SlotName))
            return $"{monsterId}:{reference.SlotName}";

        if (reference.CombatId.HasValue)
            return $"{monsterId}:combat:{reference.CombatId.Value}";

        return $"{monsterId}:index:{reference.MonsterInstanceIndex}";
    }

    private static string DescribeCreatureForLog(Creature creature)
    {
        if (creature.Player != null)
            return $"player:{creature.Player.NetId}";

        var monsterId = creature.Monster?.Id.Entry ?? "unknown";
        if (creature.PetOwner != null)
            return $"{monsterId}:pet:{creature.PetOwner.NetId}";

        return string.IsNullOrWhiteSpace(creature.SlotName) ? monsterId : $"{monsterId}:{creature.SlotName}";
    }
}
