using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
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
using MegaCrit.Sts2.Core.Models.Orbs;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Orbs;
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
        NormalizeHookSensitivePlayerStateForMultiplayerRestore(combatState, restoreContext);
        await RestoreCreatureExtrasAsync(combatState, snapshot.Creatures, restoreContext, resolutionContext);
        RestorePowerExtras(runState, combatState, snapshot.Creatures, restoreContext, resolutionContext);
        CombatManager.Instance.StateTracker.SetState(combatState);
        RestoreCombatManagerState(snapshot.CombatManager, restoreContext);
        await ReconcileCombatRoomCreatureNodes(combatState, snapshot.Creatures, restoreContext, resolutionContext);
        await SynchronizePostRestoreSpecialPowerStateAsync(combatState, snapshot.Creatures, restoreContext, resolutionContext);
        RepositionPlayersAndPetsAfterRestore(combatRoom, combatState, restoreContext);
        await TryRefreshEnemyIntentDisplaysAsync(combatState, restoreContext);
        RestoreHiddenLiveAllyVisuals(combatState, restoreContext);
        SynchronizePlayerOrbManagersForMultiplayerRestore(combatState, restoreContext);

        foreach (var player in combatState.Players)
            player.PlayerCombatState?.RecalculateCardValues();

        TryRefreshLocalCombatUi(runState, combatState, snapshot.RoundNumber, restoreContext);
        await TryRefreshLocalCombatUiAfterDelayAsync(runState, combatRoom, combatState, snapshot.RoundNumber, restoreContext);

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
            RestorePlayerOrbs(player, combat, snapshot.Orbs);
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

    private static void NormalizeHookSensitivePlayerStateForMultiplayerRestore(CombatState combatState, string restoreContext)
    {
        foreach (var player in combatState.Players)
            NormalizePlayerOrbOwnershipForMultiplayerRestore(player, restoreContext);
    }

    private static void NormalizePlayerOrbOwnershipForMultiplayerRestore(Player player, string restoreContext)
    {
        var orbQueue = player.PlayerCombatState?.OrbQueue;
        if (orbQueue == null)
            return;

        var repairedOwnerCount = 0;
        var nullOrbCount = 0;
        foreach (var orb in orbQueue.Orbs)
        {
            if (orb == null)
            {
                nullOrbCount++;
                continue;
            }

            if (ReferenceEquals(orb.Owner, player))
                continue;

            orb.Owner = player;
            repairedOwnerCount++;
        }

        if (repairedOwnerCount > 0)
        {
            MainFile.Logger.Info(
                $"Rebound {repairedOwnerCount} multiplayer orb owner reference(s) before hook-sensitive restore work for {restoreContext}. " +
                $"Player={player.NetId} Orbs={orbQueue.Orbs.Count}/{orbQueue.Capacity}");
        }

        if (nullOrbCount > 0)
        {
            MainFile.Logger.Warn(
                $"Detected {nullOrbCount} null orb slot reference(s) before hook-sensitive multiplayer restore work for {restoreContext}. " +
                $"Player={player.NetId} Orbs={orbQueue.Orbs.Count}/{orbQueue.Capacity}");
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
            ClearTransientLocalCombatCardUi();
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"Failed to clear transient multiplayer combat UI for {restoreContext}: {e.Message}");
        }

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
            RefreshLocalCombatPileUi(runState);
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"Failed to refresh local multiplayer combat piles for {restoreContext}: {e.Message}");
        }

        try
        {
            RefreshCombatUiState(combatState);
            ShowPlayerTurnBannerIfNeeded(roundNumber);
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
            MainFile.Logger.Warn($"Failed to clear stale play-queue cards during multiplayer combat UI refresh: {e.Message}");
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

    private static void SynchronizePlayerOrbManagersForMultiplayerRestore(CombatState combatState, string restoreContext)
    {
        foreach (var player in combatState.Players)
            TrySynchronizePlayerOrbManagerForMultiplayerRestore(player, restoreContext);
    }

    private static void TrySynchronizePlayerOrbManagerForMultiplayerRestore(Player player, string restoreContext)
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
                    $"Rebuilt multiplayer orb visuals after restore for {restoreContext}. " +
                    $"Player={player.NetId} Orbs={orbQueue.Orbs.Count}/{orbQueue.Capacity}");
            }
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn(
                $"Failed to rebuild multiplayer orb visuals after restore for {restoreContext}. " +
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
            AddOrbNodeToManager(orbContainer, orbNodes, NOrb.Create(orbManager.IsLocal, orb));

        for (var index = orbQueue.Orbs.Count; index < orbQueue.Capacity; index++)
            AddOrbNodeToManager(orbContainer, orbNodes, NOrb.Create(orbManager.IsLocal));

        NOrbManagerTweenLayoutMethod?.Invoke(orbManager, []);
        NOrbManagerUpdateControllerNavigationMethod?.Invoke(orbManager, []);
        orbManager.UpdateVisuals(OrbEvokeType.None);
    }

    private static void AddOrbNodeToManager(Control orbContainer, IList orbNodes, NOrb orbNode)
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
