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
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace HandsUp.HandsUpCode.Services;

public static partial class MultiplayerCombatStateSnapshotService
{
    private static readonly MethodInfo? CombatStateAddCardMethod = typeof(CombatState)
        .GetMethod("AddCard", BindingFlags.Instance | BindingFlags.NonPublic, [typeof(CardModel)]);
    private static readonly MethodInfo? HandOnCombatStateChangedMethod = typeof(NPlayerHand)
        .GetMethod("OnCombatStateChanged", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly MethodInfo? EndTurnButtonOnTurnStartedMethod = typeof(NEndTurnButton)
        .GetMethod("OnTurnStarted", BindingFlags.Instance | BindingFlags.NonPublic);

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

        var snapshot = DeserializeSnapshot(combatStateJson, restoreContext);
        if (snapshot == null)
            return false;

        var readyContext = await WaitForCombatReadyAsync(runState, restoreContext);
        if (readyContext == null)
        {
            MainFile.Logger.Warn($"Skipped applying multiplayer combat snapshot for {restoreContext} because combat was not ready.");
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
        RestoreCreatureStates(combatState, snapshot.Creatures, restoreContext, resolutionContext);
        RemoveCreaturesMissingFromSnapshot(combatState, snapshot, restoreContext, resolutionContext);
        RestorePlayerStates(runState, snapshot.Players);
        await RestoreCreatureExtrasAsync(combatState, snapshot.Creatures, restoreContext, resolutionContext);
        RestorePowerExtras(runState, combatState, snapshot.Creatures, restoreContext, resolutionContext);
        CombatManager.Instance.StateTracker.SetState(combatState);
        RestoreCombatManagerState(snapshot.CombatManager, restoreContext);
        await ReconcileCombatRoomCreatureNodes(combatState, snapshot.Creatures, restoreContext, resolutionContext);
        TryRefreshLocalCombatUi(runState, combatState, snapshot.RoundNumber, restoreContext);
        await TryRefreshLocalCombatUiAfterDelayAsync(runState, combatRoom, combatState, snapshot.RoundNumber, restoreContext);
        await SynchronizePostRestoreSpecialPowerStateAsync(combatState, snapshot.Creatures, restoreContext, resolutionContext);
        RestoreHiddenLiveAllyVisuals(combatState, restoreContext);

        foreach (var player in combatState.Players)
            player.PlayerCombatState?.RecalculateCardValues();

        MainFile.Logger.Info($"Applied multiplayer combat snapshot for {restoreContext}.");
        return true;
    }

    public static async Task TryRefreshCurrentEnemyIntentDisplaysAsync(RunState? runState, string restoreContext)
    {
        if (runState?.CurrentRoom is not CombatRoom combatRoom || combatRoom.IsPreFinished)
            return;

        try
        {
            await RefreshEnemyIntentDisplaysAsync(combatRoom.CombatState);
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"Failed to refresh multiplayer enemy intents for {restoreContext}: {e.Message}");
        }
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
            MainFile.Logger.Warn($"Failed to parse multiplayer combat snapshot for {restoreContext}: {e.Message}");
            return null;
        }
    }

    private static async Task<ReadyMultiplayerCombatRestoreContext?> WaitForCombatReadyAsync(RunState runState, string restoreContext)
    {
        var startedAt = Time.GetTicksMsec();
        while (Time.GetTicksMsec() - startedAt < CombatReadyTimeoutMs)
        {
            if (TryGetReadyRestoreContext(runState, out var readyContext))
            {
                MainFile.Logger.Info($"Multiplayer combat restore became UI-ready after {Time.GetTicksMsec() - startedAt} ms for {restoreContext}.");
                return readyContext;
            }

            await Task.Delay(CombatReadyPollIntervalMs);
        }

        MainFile.Logger.Warn($"Timed out while waiting for multiplayer combat UI readiness before applying snapshot for {restoreContext}.");
        return null;
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
                MainFile.Logger.Warn($"Skipped multiplayer combat creature snapshot restore because the target creature could not be resolved. Monster={snapshot.MonsterId}, Player={snapshot.PlayerId}");
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
                MainFile.Logger.Warn($"Skipped multiplayer combat player snapshot restore because player {snapshot.PlayerId} was not found.");
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
        var usedDeckCards = new HashSet<CardModel>();
        foreach (var existingCard in combat.AllPiles.SelectMany(pile => pile.Cards).ToList())
            existingCard.RemoveFromState();

        foreach (var pileSnapshot in pileSnapshots)
        {
            var targetPile = CardPile.Get(pileSnapshot.PileType, player);
            if (targetPile == null)
                continue;

            foreach (var cardSnapshot in pileSnapshot.Cards)
            {
                var deckVersion = ResolveDeckVersion(runState, player, cardSnapshot, usedDeckCards);
                var loadableCard = MultiplayerSerializableCardStateService.ResolveCardForRestore(
                    cardSnapshot.Card,
                    player,
                    deckVersion?.ToSerializable() ?? cardSnapshot.DeckVersionCard);
                var card = runState.LoadCard(loadableCard, player);
                RegisterCardInCombatState(card);
                card.DeckVersion = deckVersion;
                ApplyCardSnapshotState(card, cardSnapshot);

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

    private static void TryRefreshLocalCombatUi(RunState runState, CombatState combatState, string restoreContext)
    {
        TryRefreshLocalCombatUi(runState, combatState, combatState.RoundNumber, restoreContext);
    }

    private static void TryRefreshLocalCombatUi(RunState runState, CombatState combatState, int roundNumber, string restoreContext)
    {
        try
        {
            RebuildLocalHandUi(runState);
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"Failed to rebuild local multiplayer hand UI for {restoreContext}: {e.Message}");
        }

        try
        {
            var handUi = NPlayerHand.Instance;
            if (handUi != null)
                HandOnCombatStateChangedMethod?.Invoke(handUi, [combatState]);

            var endTurnButton = NCombatRoom.Instance?.Ui?.EndTurnButton;
            if (endTurnButton != null && combatState.CurrentSide == CombatSide.Player)
                EndTurnButtonOnTurnStartedMethod?.Invoke(endTurnButton, [combatState]);
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"Failed to refresh local multiplayer combat UI for {restoreContext}: {e.Message}");
        }
    }

    private static void RebuildLocalHandUi(RunState runState)
    {
        var handUi = NPlayerHand.Instance;
        var localPlayer = MegaCrit.Sts2.Core.Context.LocalContext.GetMe(runState);
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

    public sealed partial class CombatStateSnapshot
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

    public sealed partial class CombatCreatureSnapshot
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

    public sealed partial class CombatPowerSnapshot
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

    public sealed partial class CombatPlayerSnapshot
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
                MaxStars = state.maxStars,
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

    public sealed partial class CombatPileSnapshot
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

    public sealed partial class CombatCardSnapshot
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

    public sealed partial class CombatPotionSnapshot
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

    public sealed partial class CombatRelicSnapshot
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

    public sealed partial class CombatOrbSnapshot
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
