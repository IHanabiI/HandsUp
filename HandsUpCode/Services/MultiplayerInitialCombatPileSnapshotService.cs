using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

namespace HandsUp.HandsUpCode.Services;

public static class MultiplayerInitialCombatPileSnapshotService
{
    private const string SnapshotFileName = "handsup_multiplayer_initial_combat_piles.json";

    private static readonly MethodInfo? CombatStateAddCardMethod = typeof(CombatState)
        .GetMethod("AddCard", BindingFlags.Instance | BindingFlags.NonPublic, [typeof(CardModel)]);
    private static readonly MethodInfo? HandOnCombatStateChangedMethod = typeof(NPlayerHand)
        .GetMethod("OnCombatStateChanged", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly MethodInfo? EndTurnButtonOnTurnStartedMethod = typeof(NEndTurnButton)
        .GetMethod("OnTurnStarted", BindingFlags.Instance | BindingFlags.NonPublic);
    private static PendingInitialDrawOverride? _pendingInitialDrawOverride;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    private static string GetSnapshotPath()
    {
        return UserDataPathProvider.GetProfileScopedPath(
            SaveManager.Instance.CurrentProfileId,
            Path.Combine(UserDataPathProvider.SavesDir, SnapshotFileName));
    }

    public static void CaptureSnapshotFromCurrentState(RunManager? runManager, string sourceTag)
    {
        var payload = BuildSnapshot(runManager);
        if (payload == null)
            return;

        var existingSnapshot = TryReadSnapshot();
        if (existingSnapshot != null && existingSnapshot.RoomScope == payload.RoomScope)
        {
            MainFile.Logger.Info($"Skipped multiplayer initial combat pile snapshot capture for {payload.RoomScope} because the first-entry snapshot already exists.");
            return;
        }

        var snapshotDirectory = Path.GetDirectoryName(ProjectSettings.GlobalizePath(GetSnapshotPath()));
        if (!string.IsNullOrWhiteSpace(snapshotDirectory))
            Directory.CreateDirectory(snapshotDirectory);

        var snapshotJson = JsonSerializer.Serialize(payload, JsonOptions);
        var snapshotPath = ProjectSettings.GlobalizePath(GetSnapshotPath());
        File.WriteAllText(snapshotPath, snapshotJson, Encoding.UTF8);

        MainFile.Logger.Info($"Multiplayer initial combat pile snapshot written from {sourceTag} to {snapshotPath}");
    }

    public static string? TryReadSnapshotJsonForCurrentRoom(RunManager? runManager)
    {
        var runState = runManager?.DebugOnlyGetState();
        var currentRoom = runState?.CurrentRoom;
        if (runState == null || currentRoom is not CombatRoom)
            return null;

        if (MultiplayerSoftRestartRoomClassifier.IsEventScopedRoom(runState))
            return null;

        var snapshot = TryReadSnapshot();
        if (snapshot == null)
            return null;

        return snapshot.RoomScope == BuildRoomScope(runState, currentRoom)
            ? JsonSerializer.Serialize(snapshot, JsonOptions)
            : null;
    }

    public static bool ArmSnapshotJsonForNextInitialDraw(string? snapshotJson, string restoreContext)
    {
        if (string.IsNullOrWhiteSpace(snapshotJson))
            return false;

        MultiplayerCombatPileSnapshot? snapshot;
        try
        {
            snapshot = JsonSerializer.Deserialize<MultiplayerCombatPileSnapshot>(snapshotJson, JsonOptions);
        }
        catch (JsonException e)
        {
            MainFile.Logger.Warn($"Failed to parse multiplayer initial combat pile snapshot for {restoreContext}: {e.Message}");
            return false;
        }

        if (snapshot == null)
            return false;

        _pendingInitialDrawOverride = new PendingInitialDrawOverride(snapshot, restoreContext);
        MainFile.Logger.Info($"Armed multiplayer initial combat pile snapshot for {restoreContext}.");
        return true;
    }

    public static async Task<bool> ApplySnapshotJsonPostLoadAsync(string? snapshotJson, RunState runState, string restoreContext)
    {
        if (string.IsNullOrWhiteSpace(snapshotJson))
            return false;

        MultiplayerCombatPileSnapshot? snapshot;
        try
        {
            snapshot = JsonSerializer.Deserialize<MultiplayerCombatPileSnapshot>(snapshotJson, JsonOptions);
        }
        catch (JsonException e)
        {
            MainFile.Logger.Warn($"Failed to parse multiplayer initial combat pile snapshot for {restoreContext}: {e.Message}");
            return false;
        }

        if (snapshot == null)
            return false;

        await WaitForCombatReadyAsync(runState, restoreContext);

        var combatRoom = runState.CurrentRoom as CombatRoom;
        var combatState = combatRoom?.CombatState;
        if (combatState == null || !CombatManager.Instance.IsInProgress)
        {
            MainFile.Logger.Warn($"Skipped applying multiplayer initial combat pile snapshot for {restoreContext} because combat is not ready.");
            return false;
        }

        if (DoCurrentPilesAlreadyMatchSnapshot(runState, snapshot))
        {
            MainFile.Logger.Info($"Skipped applying multiplayer initial combat pile snapshot for {restoreContext} because the current piles already match the first-entry snapshot.");
            return true;
        }

        foreach (var playerSnapshot in snapshot.Players)
        {
            var player = runState.GetPlayer(playerSnapshot.PlayerId);
            if (player == null || player.PlayerCombatState == null)
                continue;

            RestorePlayerPiles(runState, player, playerSnapshot.Piles);
        }

        CombatManager.Instance.StateTracker.SetState(combatState);
        TryRefreshLocalCombatUi(runState, combatState, restoreContext);

        foreach (var player in combatState.Players)
            player.PlayerCombatState?.RecalculateCardValues();

        MainFile.Logger.Info($"Applied multiplayer initial combat pile snapshot for {restoreContext}.");
        return true;
    }

    public static void ClearSnapshot(string reason)
    {
        var snapshotPath = ProjectSettings.GlobalizePath(GetSnapshotPath());
        if (!File.Exists(snapshotPath))
            return;

        File.Delete(snapshotPath);
        MainFile.Logger.Info($"Cleared multiplayer initial combat pile snapshot: {reason}");
    }

    public static void ClearPendingInitialDrawOverride(string reason)
    {
        if (_pendingInitialDrawOverride == null)
            return;

        _pendingInitialDrawOverride = null;
        MainFile.Logger.Info($"Cleared pending multiplayer initial draw override: {reason}");
    }

    private static MultiplayerCombatPileSnapshot? BuildSnapshot(RunManager? runManager)
    {
        var runState = runManager?.DebugOnlyGetState();
        var combatRoom = runState?.CurrentRoom as CombatRoom;
        if (runState == null || combatRoom == null || combatRoom.IsPreFinished)
            return null;

        if (MultiplayerSoftRestartRoomClassifier.IsEventScopedRoom(runState))
        {
            MainFile.Logger.Info("Skipped multiplayer initial combat pile snapshot capture because the current room is still event-scoped.");
            return null;
        }

        var combatState = combatRoom.CombatState;
        if (combatState.CurrentSide != CombatSide.Player || combatState.RoundNumber != 1)
            return null;

        var netState = NetFullCombatState.FromRun(runState, null);
        return new MultiplayerCombatPileSnapshot
        {
            RoomScope = BuildRoomScope(runState, combatRoom),
            Players = netState.Players.Select(playerState => new MultiplayerCombatPlayerPileSnapshot
            {
                PlayerId = playerState.playerId,
                Piles = playerState.piles.Select(CombatStateSnapshotService.CombatPileSnapshot.FromNetState).ToList()
            }).ToList()
        };
    }

    private static MultiplayerCombatPileSnapshot? TryReadSnapshot()
    {
        var snapshotPath = ProjectSettings.GlobalizePath(GetSnapshotPath());
        if (!File.Exists(snapshotPath))
            return null;

        try
        {
            return JsonSerializer.Deserialize<MultiplayerCombatPileSnapshot>(File.ReadAllText(snapshotPath, Encoding.UTF8), JsonOptions);
        }
        catch (JsonException e)
        {
            MainFile.Logger.Warn($"Failed to parse multiplayer initial combat pile snapshot {snapshotPath}: {e.Message}");
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
                turnStartedCompletion?.TrySetResult(true);
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
                MainFile.Logger.Warn($"Timed out while waiting for combat play phase before applying multiplayer initial combat pile snapshot for {restoreContext}.");
        }
        finally
        {
            CombatManager.Instance.TurnStarted -= OnTurnStarted;
        }
    }

    public static void ApplyPendingInitialDrawOverrideIfNeeded(Player player, bool fromHandDraw)
    {
        if (!fromHandDraw || _pendingInitialDrawOverride == null)
            return;

        var runState = RunManager.Instance.DebugOnlyGetState();
        var combatRoom = runState?.CurrentRoom as CombatRoom;
        var combatState = combatRoom?.CombatState;
        if (runState == null || combatState == null || combatState.RoundNumber != 1 || combatState.CurrentSide != CombatSide.Player)
            return;

        if (MultiplayerSoftRestartRoomClassifier.IsEventScopedRoom(runState))
        {
            MainFile.Logger.Info($"Clearing pending multiplayer initial draw override for {_pendingInitialDrawOverride.RestoreContext} because the restored room is event-scoped.");
            _pendingInitialDrawOverride = null;
            return;
        }

        var playerSnapshot = _pendingInitialDrawOverride.Snapshot.Players.FirstOrDefault(snapshot => snapshot.PlayerId == player.NetId);
        if (playerSnapshot == null || player.PlayerCombatState == null)
            return;

        if (DoCurrentPilesAlreadyMatchSnapshot(runState, _pendingInitialDrawOverride.Snapshot))
        {
            MainFile.Logger.Info($"Skipping pending multiplayer initial draw override for {_pendingInitialDrawOverride.RestoreContext} because current piles already match.");
            _pendingInitialDrawOverride = null;
            return;
        }

        ApplyPreDrawPiles(runState, player, playerSnapshot.Piles);
        _pendingInitialDrawOverride.AppliedPlayerIds.Add(player.NetId);

        if (_pendingInitialDrawOverride.Snapshot.Players.All(snapshot => _pendingInitialDrawOverride.AppliedPlayerIds.Contains(snapshot.PlayerId)))
        {
            MainFile.Logger.Info($"Finished applying pending multiplayer initial draw override for {_pendingInitialDrawOverride.RestoreContext}.");
            _pendingInitialDrawOverride = null;
        }
    }

    private static void RestorePlayerPiles(RunState runState, Player player, System.Collections.Generic.IReadOnlyList<CombatStateSnapshotService.CombatPileSnapshot> pileSnapshots)
    {
        var combat = player.PlayerCombatState!;
        foreach (var existingCard in combat.AllPiles.SelectMany(pile => pile.Cards).ToList())
            existingCard.RemoveFromState();

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

    private static void ApplyPreDrawPiles(RunState runState, Player player, System.Collections.Generic.IReadOnlyList<CombatStateSnapshotService.CombatPileSnapshot> pileSnapshots)
    {
        var handSnapshot = pileSnapshots.FirstOrDefault(pile => pile.PileType == PileType.Hand);
        var preDrawSnapshots = pileSnapshots
            .Where(pile => pile.PileType != PileType.Hand)
            .Select(pile => new CombatStateSnapshotService.CombatPileSnapshot
            {
                PileType = pile.PileType,
                Cards = pile.PileType == PileType.Draw
                    ? [.. (handSnapshot?.Cards ?? []), .. pile.Cards]
                    : [.. pile.Cards]
            })
            .ToList();

        if (preDrawSnapshots.All(pile => pile.PileType != PileType.Draw))
        {
            preDrawSnapshots.Add(new CombatStateSnapshotService.CombatPileSnapshot
            {
                PileType = PileType.Draw,
                Cards = [.. (handSnapshot?.Cards ?? [])]
            });
        }

        RestorePlayerPiles(runState, player, preDrawSnapshots);
    }

    private static bool DoCurrentPilesAlreadyMatchSnapshot(RunState runState, MultiplayerCombatPileSnapshot snapshot)
    {
        var netState = NetFullCombatState.FromRun(runState, null);
        foreach (var playerSnapshot in snapshot.Players)
        {
            var hasCurrentPlayerState = netState.Players.Any(player => player.playerId == playerSnapshot.PlayerId);
            if (!hasCurrentPlayerState)
                return false;

            var currentPlayerState = netState.Players.First(player => player.playerId == playerSnapshot.PlayerId);
            var currentPiles = currentPlayerState.piles.Select(CombatStateSnapshotService.CombatPileSnapshot.FromNetState).ToList();
            if (!ArePileSnapshotsEquivalent(currentPiles, playerSnapshot.Piles))
                return false;
        }

        return true;
    }

    private static bool ArePileSnapshotsEquivalent(
        System.Collections.Generic.IReadOnlyList<CombatStateSnapshotService.CombatPileSnapshot> currentPiles,
        System.Collections.Generic.IReadOnlyList<CombatStateSnapshotService.CombatPileSnapshot> targetPiles)
    {
        if (currentPiles.Count != targetPiles.Count)
            return false;

        for (var pileIndex = 0; pileIndex < currentPiles.Count; pileIndex++)
        {
            var currentPile = currentPiles[pileIndex];
            var targetPile = targetPiles[pileIndex];
            if (currentPile.PileType != targetPile.PileType)
                return false;

            if (currentPile.Cards.Count != targetPile.Cards.Count)
                return false;

            for (var cardIndex = 0; cardIndex < currentPile.Cards.Count; cardIndex++)
            {
                if (!AreCardSnapshotsEquivalent(currentPile.Cards[cardIndex], targetPile.Cards[cardIndex]))
                    return false;
            }
        }

        return true;
    }

    private static bool AreCardSnapshotsEquivalent(
        CombatStateSnapshotService.CombatCardSnapshot currentCard,
        CombatStateSnapshotService.CombatCardSnapshot targetCard)
    {
        if (!string.Equals(JsonSerializer.Serialize(currentCard.Card, JsonOptions), JsonSerializer.Serialize(targetCard.Card, JsonOptions), System.StringComparison.Ordinal))
            return false;

        if (!Nullable.Equals(currentCard.Affliction, targetCard.Affliction))
            return false;

        if (currentCard.AfflictionCount != targetCard.AfflictionCount)
            return false;

        var currentKeywords = currentCard.Keywords ?? [];
        var targetKeywords = targetCard.Keywords ?? [];
        if (currentKeywords.Count != targetKeywords.Count)
            return false;

        for (var index = 0; index < currentKeywords.Count; index++)
        {
            if (currentKeywords[index] != targetKeywords[index])
                return false;
        }

        return true;
    }

    private static void RegisterCardInCombatState(CardModel card)
    {
        CombatStateAddCardMethod?.Invoke(card.Owner?.Creature.CombatState, [card]);
    }

    private static void TryRefreshLocalCombatUi(RunState runState, CombatState combatState, string restoreContext)
    {
        try
        {
            RebuildLocalHandUi(runState);
        }
        catch (System.Exception e)
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
        catch (System.Exception e)
        {
            MainFile.Logger.Warn($"Failed to refresh local multiplayer combat UI for {restoreContext}: {e.Message}");
        }
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

    private static string BuildRoomScope(RunState runState, AbstractRoom currentRoom)
    {
        var roomIdentity = currentRoom.ModelId?.ToString() ?? currentRoom.RoomType.ToString();
        return $"{runState.TotalFloor:D4}_{roomIdentity}";
    }

    private sealed class MultiplayerCombatPileSnapshot
    {
        public string RoomScope { get; set; } = string.Empty;
        public System.Collections.Generic.List<MultiplayerCombatPlayerPileSnapshot> Players { get; set; } = [];
    }

    private sealed class MultiplayerCombatPlayerPileSnapshot
    {
        public ulong PlayerId { get; set; }
        public System.Collections.Generic.List<CombatStateSnapshotService.CombatPileSnapshot> Piles { get; set; } = [];
    }

    private sealed class PendingInitialDrawOverride
    {
        public PendingInitialDrawOverride(MultiplayerCombatPileSnapshot snapshot, string restoreContext)
        {
            Snapshot = snapshot;
            RestoreContext = restoreContext;
        }

        public MultiplayerCombatPileSnapshot Snapshot { get; }
        public string RestoreContext { get; }
        public System.Collections.Generic.HashSet<ulong> AppliedPlayerIds { get; } = [];
    }
}
