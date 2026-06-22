using System.Text.Json;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace HandsUp.HandsUpCode.Services;

internal static class SingleplayerSerializableCardStateService
{
    public static SerializableCard CaptureCombatCard(SerializableCard netStateCard, CardModel? actualCard, Player? owner)
    {
        var primary = Clone(actualCard?.ToSerializable() ?? netStateCard);
        if (HasSavedProperties(primary.Props))
            return primary;

        var fallback = ResolveFallbackSerializableCard(actualCard, owner, primary);
        return MergeMissingState(primary, fallback);
    }

    public static SerializableCard? CaptureLiveCard(CardModel? card)
    {
        if (card == null)
            return null;

        var primary = Clone(card.ToSerializable());
        if (HasSavedProperties(primary.Props))
            return primary;

        var fallback = ResolveFallbackSerializableCard(card, card.Owner, primary);
        return MergeMissingState(primary, fallback);
    }

    public static SerializableCard ResolveCardForRestore(
        SerializableCard snapshotCard,
        Player? owner,
        SerializableCard? preferredDeckCard = null)
    {
        var primary = Clone(snapshotCard);
        var fallback = ResolveRestoreFallbackSerializableCard(primary, owner, preferredDeckCard);
        return fallback == null ? primary : MergeMissingState(primary, fallback);
    }

    public static bool MatchesDeckCardForRestore(SerializableCard candidate, SerializableCard desired)
    {
        if (!HasCompatibleDeckIdentity(candidate, desired))
            return false;

        if (!HasSavedProperties(desired.Props))
            return true;

        return AreSerializedObjectsEquivalent(candidate.Props, desired.Props);
    }

    private static SerializableCard ResolveFallbackSerializableCard(CardModel? actualCard, Player? owner, SerializableCard primary)
    {
        var deckVersionSerializable = actualCard?.DeckVersion?.ToSerializable();
        if (CanUseDeckVersionFallback(primary, deckVersionSerializable))
            return Clone(deckVersionSerializable!);

        if (owner == null)
            return primary;

        foreach (var deckCard in owner.Deck.Cards)
        {
            var candidate = deckCard.ToSerializable();
            if (CanUseDeckFallback(primary, candidate))
                return Clone(candidate);
        }

        return primary;
    }

    private static SerializableCard? ResolveRestoreFallbackSerializableCard(
        SerializableCard primary,
        Player? owner,
        SerializableCard? preferredDeckCard)
    {
        if (CanUseRestoreFallback(primary, preferredDeckCard))
            return Clone(preferredDeckCard!);

        var desiredDeckCard = preferredDeckCard ?? primary;
        if (owner == null)
            return null;

        foreach (var deckCard in owner.Deck.Cards)
        {
            var candidate = deckCard.ToSerializable();
            if (HasCompatibleDeckIdentity(candidate, desiredDeckCard))
                return Clone(candidate);
        }

        return null;
    }

    private static SerializableCard MergeMissingState(SerializableCard primary, SerializableCard fallback)
    {
        if (!HasSavedProperties(primary.Props) && HasSavedProperties(fallback.Props))
            primary.Props = fallback.Props;

        if (primary.FloorAddedToDeck == null && fallback.FloorAddedToDeck != null)
            primary.FloorAddedToDeck = fallback.FloorAddedToDeck;

        if (primary.Enchantment == null && fallback.Enchantment != null)
            primary.Enchantment = fallback.Enchantment;

        return primary;
    }

    private static bool CanUseDeckVersionFallback(SerializableCard primary, SerializableCard? fallback)
    {
        return fallback != null
               && !HasSavedProperties(primary.Props)
               && HasSavedProperties(fallback.Props)
               && MatchesDeckCardForRestore(fallback, primary);
    }

    private static bool CanUseDeckFallback(SerializableCard primary, SerializableCard fallback)
    {
        return !HasSavedProperties(primary.Props)
               && HasSavedProperties(fallback.Props)
               && MatchesDeckCardForRestore(fallback, primary);
    }

    private static bool CanUseRestoreFallback(SerializableCard primary, SerializableCard? fallback)
    {
        return fallback != null
               && HasCompatibleDeckIdentity(fallback, primary)
               && (HasSavedProperties(fallback.Props)
                   || fallback.FloorAddedToDeck != null
                   || fallback.Enchantment != null);
    }

    private static bool HasCompatibleDeckIdentity(SerializableCard candidate, SerializableCard desired)
    {
        return Equals(candidate.Id, desired.Id)
               && candidate.CurrentUpgradeLevel == desired.CurrentUpgradeLevel
               && (!desired.FloorAddedToDeck.HasValue || candidate.FloorAddedToDeck == desired.FloorAddedToDeck)
               && (desired.Enchantment == null || AreSerializedObjectsEquivalent(candidate.Enchantment, desired.Enchantment));
    }

    private static bool AreSerializedObjectsEquivalent(object? left, object? right)
    {
        return JsonSerializer.Serialize(left) == JsonSerializer.Serialize(right);
    }

    private static bool HasSavedProperties(SavedProperties? props)
    {
        return props != null
               && ((props.ints?.Count ?? 0) > 0
                   || (props.bools?.Count ?? 0) > 0
                   || (props.strings?.Count ?? 0) > 0
                   || (props.intArrays?.Count ?? 0) > 0
                   || (props.modelIds?.Count ?? 0) > 0
                   || (props.cards?.Count ?? 0) > 0
                   || (props.cardArrays?.Count ?? 0) > 0);
    }

    private static SerializableCard Clone(SerializableCard card)
    {
        return new SerializableCard
        {
            Id = card.Id,
            CurrentUpgradeLevel = card.CurrentUpgradeLevel,
            Enchantment = card.Enchantment,
            Props = card.Props,
            FloorAddedToDeck = card.FloorAddedToDeck
        };
    }
}
