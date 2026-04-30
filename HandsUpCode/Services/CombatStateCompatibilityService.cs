using BaseLib.Utils;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;

namespace HandsUp.HandsUpCode.Services;

public static class CombatStateCompatibilityService
{
    public static CombatStateWrapper? Wrap(Creature? creature)
    {
        return creature == null ? null : BetaMainCompatibility.Creature_.WrappedCombatState(creature);
    }

    public static CombatStateWrapper? Wrap(CardModel? card)
    {
        return card == null ? null : BetaMainCompatibility.CardModel_.WrappedCombatState(card);
    }

    public static object? GetRawCombatState(Creature? creature)
    {
        return creature == null ? null : BetaMainCompatibility.Creature_.CombatState.Get(creature);
    }

    public static object? GetRawCombatState(CardModel? card)
    {
        return card == null ? null : BetaMainCompatibility.CardModel_.CombatState.Get(card);
    }

    public static CombatState? GetCombatState(Creature? creature)
    {
        return GetRawCombatState(creature) as CombatState;
    }

    public static CombatState? GetCombatState(CardModel? card)
    {
        return GetRawCombatState(card) as CombatState;
    }

    public static bool BelongsToCombatState(Creature? creature, CombatState? combatState)
    {
        return creature != null
               && combatState != null
               && ReferenceEquals(GetRawCombatState(creature), combatState);
    }
}
