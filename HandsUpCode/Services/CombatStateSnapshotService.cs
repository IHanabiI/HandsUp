using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Orbs;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace HandsUp.HandsUpCode.Services;

public static class CombatStateSnapshotService
{
    private static readonly MethodInfo? CombatStateAddCardMethod = typeof(CombatState)
        .GetMethod("AddCard", BindingFlags.Instance | BindingFlags.NonPublic, [typeof(CardModel)]);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    public static string? CaptureCurrentCombatStateJson(RunState? runState)
    {
        if (runState?.CurrentRoom is not CombatRoom combatRoom || combatRoom.IsPreFinished)
            return null;

        var netState = NetFullCombatState.FromRun(runState, null);
        var snapshot = CombatStateSnapshot.FromNetState(netState);
        return JsonSerializer.Serialize(snapshot, JsonOptions);
    }

    public static async Task<bool> TryApplyCombatStateJsonAsync(string? combatStateJson, RunState runState, string restoreContext)
    {
        if (string.IsNullOrWhiteSpace(combatStateJson))
            return false;

        var snapshot = DeserializeSnapshot(combatStateJson, restoreContext);
        if (snapshot == null)
            return false;

        await WaitForCombatReadyAsync(runState, restoreContext);

        var combatRoom = runState.CurrentRoom as CombatRoom;
        var combatState = combatRoom?.CombatState;
        if (combatState == null || !CombatManager.Instance.IsInProgress)
        {
            MainFile.Logger.Warn($"Skipped applying combat snapshot for {restoreContext} because no active combat state is available.");
            return false;
        }

        runState.Rng.LoadFromSerializable(snapshot.Rng);
        RunManager.Instance.PlayerChoiceSynchronizer.FastForwardChoiceIds(snapshot.NextChoiceIds);

        if (snapshot.LastExecutedActionId.HasValue)
            RunManager.Instance.ActionQueueSet.FastForwardNextActionId(snapshot.LastExecutedActionId.Value + 1);

        if (snapshot.LastExecutedHookId.HasValue)
            RunManager.Instance.ActionQueueSynchronizer.FastForwardHookId(snapshot.LastExecutedHookId.Value + 1);

        combatState.RoundNumber = snapshot.RoundNumber;
        combatState.CurrentSide = snapshot.CurrentSide;

        RestoreCreatureStates(combatState, snapshot.Creatures);
        RestorePlayerStates(runState, snapshot.Players);
        CombatManager.Instance.StateTracker.SetState(combatState);

        foreach (var player in combatState.Players)
        {
            player.PlayerCombatState?.RecalculateCardValues();
        }

        MainFile.Logger.Info($"Applied full combat snapshot for {restoreContext}.");
        return true;
    }

    private static CombatStateSnapshot? DeserializeSnapshot(string? combatStateJson, string restoreContext)
    {
        if (string.IsNullOrWhiteSpace(combatStateJson))
            return null;

        try
        {
            return JsonSerializer.Deserialize<CombatStateSnapshot>(combatStateJson, JsonOptions);
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"Failed to parse combat snapshot for {restoreContext}: {e.Message}");
            return null;
        }
    }

    private static async Task WaitForCombatReadyAsync(RunState runState, string restoreContext)
    {
        if (runState.CurrentRoom is not CombatRoom)
            return;

        TaskCompletionSource<bool>? turnStartedCompletion = null;
        void OnTurnStarted(CombatState state)
        {
            if (state.CurrentSide == CombatSide.Player)
            {
                turnStartedCompletion?.TrySetResult(true);
            }
        }

        if (CombatManager.Instance.IsInProgress
            && RunManager.Instance.ActionQueueSynchronizer.CombatState == ActionSynchronizerCombatState.PlayPhase
            && !RunManager.Instance.ActionExecutor.IsPaused)
        {
            return;
        }

        try
        {
            turnStartedCompletion = new TaskCompletionSource<bool>();
            CombatManager.Instance.TurnStarted += OnTurnStarted;

            var timeoutTask = Task.Delay(5000);
            var completedTask = await Task.WhenAny(turnStartedCompletion.Task, timeoutTask);
            if (completedTask == timeoutTask)
            {
                MainFile.Logger.Warn($"Timed out while waiting for combat play phase before applying snapshot for {restoreContext}.");
            }
        }
        finally
        {
            CombatManager.Instance.TurnStarted -= OnTurnStarted;
        }
    }

    private static void RestoreCreatureStates(CombatState combatState, IReadOnlyList<CombatCreatureSnapshot> snapshots)
    {
        var playerLookup = combatState.Creatures
            .Where(creature => creature.Player != null)
            .ToDictionary(creature => creature.Player!.NetId);

        var monsterQueues = combatState.Creatures
            .Where(creature => creature.Player == null)
            .GroupBy(creature => creature.Monster?.Id.ToString() ?? string.Empty)
            .ToDictionary(group => group.Key, group => new Queue<Creature>(group));

        foreach (var snapshot in snapshots)
        {
            Creature? creature = null;
            if (snapshot.PlayerId.HasValue)
            {
                playerLookup.TryGetValue(snapshot.PlayerId.Value, out creature);
            }
            else
            {
                var monsterKey = snapshot.MonsterId?.ToString() ?? string.Empty;
                if (monsterQueues.TryGetValue(monsterKey, out var queue) && queue.Count > 0)
                    creature = queue.Dequeue();
            }

            if (creature == null)
            {
                MainFile.Logger.Warn($"Skipped combat creature snapshot restore because the target creature could not be resolved. Monster={snapshot.MonsterId}, Player={snapshot.PlayerId}");
                continue;
            }

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
                    MainFile.Logger.Warn($"Skipped restoring multiplayer power {powerSnapshot.Id} x{powerSnapshot.Amount} for creature {(snapshot.PlayerId?.ToString() ?? snapshot.MonsterId?.ToString() ?? "unknown")} because restore failed: {e.Message}");
                }
            }
        }
    }

    private static void RestorePlayerStates(RunState runState, IReadOnlyList<CombatPlayerSnapshot> snapshots)
    {
        foreach (var snapshot in snapshots)
        {
            var player = runState.GetPlayer(snapshot.PlayerId);
            if (player == null)
            {
                MainFile.Logger.Warn($"Skipped combat player snapshot restore because player {snapshot.PlayerId} was not found.");
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
            RestorePlayerOrbs(combat, snapshot.Orbs);
        }
    }

    private static void RestorePlayerPiles(RunState runState, Player player, IReadOnlyList<CombatPileSnapshot> pileSnapshots)
    {
        var combat = player.PlayerCombatState!;
        foreach (var existingCard in combat.AllPiles.SelectMany(pile => pile.Cards).ToList())
        {
            existingCard.RemoveFromState();
        }

        foreach (var pileSnapshot in pileSnapshots)
        {
            var targetPile = CardPile.Get(pileSnapshot.PileType, player);
            if (targetPile == null)
                continue;

            foreach (var cardSnapshot in pileSnapshot.Cards)
            {
                var card = runState.LoadCard(cardSnapshot.Card, player);
                RegisterCardInCombatState(card);
                card.DeckVersion = null;

                if (cardSnapshot.Affliction != null)
                {
                    var affliction = ModelDb.GetById<AfflictionModel>(cardSnapshot.Affliction).ToMutable();
                    card.AfflictInternal(affliction, cardSnapshot.AfflictionCount);
                }

                if (cardSnapshot.Keywords != null)
                {
                    foreach (var keyword in cardSnapshot.Keywords)
                    {
                        if (!card.Keywords.Contains(keyword))
                            card.AddKeyword(keyword);
                    }
                }

                targetPile.AddInternal(card, -1, false);
            }
        }
    }

    private static void RegisterCardInCombatState(CardModel card)
    {
        CombatStateAddCardMethod?.Invoke(CombatStateCompatibilityService.GetRawCombatState(card.Owner?.Creature), [card]);
    }

    private static void RestorePlayerOrbs(PlayerCombatState combat, IReadOnlyList<CombatOrbSnapshot> orbSnapshots)
    {
        var orbQueue = combat.OrbQueue;
        var capacity = orbQueue.Capacity;
        orbQueue.Clear();
        orbQueue.AddCapacity(capacity);

        for (var index = 0; index < orbSnapshots.Count; index++)
        {
            var orbSnapshot = orbSnapshots[index];
            var orb = ModelDb.GetById<OrbModel>(orbSnapshot.Id).ToMutable();
            orbQueue.Insert(index, orb);
        }
    }

    public sealed class CombatStateSnapshot
    {
        public List<CombatCreatureSnapshot> Creatures { get; set; } = [];
        public List<CombatPlayerSnapshot> Players { get; set; } = [];
        public SerializableRunRngSet Rng { get; set; } = new();
        public int RoundNumber { get; set; }
        public CombatSide CurrentSide { get; set; }
        public List<uint> NextChoiceIds { get; set; } = [];
        public uint? LastExecutedHookId { get; set; }
        public uint? LastExecutedActionId { get; set; }

        public static CombatStateSnapshot FromNetState(NetFullCombatState state)
        {
            var combatState = (RunManager.Instance.DebugOnlyGetState()?.CurrentRoom as CombatRoom)?.CombatState;
            return new CombatStateSnapshot
            {
                Creatures = state.Creatures.Select(CombatCreatureSnapshot.FromNetState).ToList(),
                Players = state.Players.Select(CombatPlayerSnapshot.FromNetState).ToList(),
                Rng = state.Rng,
                RoundNumber = combatState?.RoundNumber ?? 1,
                CurrentSide = combatState?.CurrentSide ?? CombatSide.Player,
                NextChoiceIds = state.nextChoiceIds.ToList(),
                LastExecutedHookId = state.lastExecutedHookId,
                LastExecutedActionId = state.lastExecutedActionId
            };
        }
    }

    public sealed class CombatCreatureSnapshot
    {
        public ModelId? MonsterId { get; set; }
        public ulong? PlayerId { get; set; }
        public int CurrentHp { get; set; }
        public int MaxHp { get; set; }
        public int Block { get; set; }
        public List<CombatPowerSnapshot> Powers { get; set; } = [];

        public static CombatCreatureSnapshot FromNetState(NetFullCombatState.CreatureState state)
        {
            return new CombatCreatureSnapshot
            {
                MonsterId = state.monsterId,
                PlayerId = state.playerId,
                CurrentHp = state.currentHp,
                MaxHp = state.maxHp,
                Block = state.block,
                Powers = state.powers.Select(CombatPowerSnapshot.FromNetState).ToList()
            };
        }
    }

    public sealed class CombatPowerSnapshot
    {
        public ModelId Id { get; set; }
        public int Amount { get; set; }

        public static CombatPowerSnapshot FromNetState(NetFullCombatState.PowerState state)
        {
            return new CombatPowerSnapshot
            {
                Id = state.id,
                Amount = state.amount
            };
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

        public static CombatPlayerSnapshot FromNetState(NetFullCombatState.PlayerState state)
        {
            return new CombatPlayerSnapshot
            {
                PlayerId = state.playerId,
                CharacterId = state.characterId,
                Energy = state.energy,
                Stars = state.stars,
                MaxStars = state.stars,
                MaxPotionCount = state.maxPotionCount,
                Gold = state.gold,
                Piles = state.piles.Select(CombatPileSnapshot.FromNetState).ToList(),
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

        public static CombatPileSnapshot FromNetState(NetFullCombatState.CombatPileState state)
        {
            return new CombatPileSnapshot
            {
                PileType = state.pileType,
                Cards = state.cards.Select(CombatCardSnapshot.FromNetState).ToList()
            };
        }
    }

    public sealed class CombatCardSnapshot
    {
        public SerializableCard Card { get; set; } = new();
        public ModelId? Affliction { get; set; }
        public int AfflictionCount { get; set; }
        public List<CardKeyword>? Keywords { get; set; }

        public static CombatCardSnapshot FromNetState(NetFullCombatState.CardState state)
        {
            return new CombatCardSnapshot
            {
                Card = state.card,
                Affliction = state.affliction,
                AfflictionCount = state.afflictionCount,
                Keywords = state.keywords?.ToList()
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
}
