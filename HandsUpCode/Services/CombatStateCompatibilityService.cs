using BaseLib.Utils;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using System.Reflection;

namespace HandsUp.HandsUpCode.Services;

public static class CombatStateCompatibilityService
{
    private static readonly MethodInfo? CombatManagerSetPhaseForAllPlayersMethod = typeof(CombatManager)
        .GetMethod("SetPhaseForAllPlayers", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    private static readonly PropertyInfo? PlayerCombatStatePhaseProperty = typeof(PlayerCombatState)
        .GetProperty("Phase", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

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

    public static CombatState? GetCombatState(ICombatState? combatState)
    {
        return combatState as CombatState;
    }

    public static CombatState? GetCurrentCombatState()
    {
        return CombatManager.Instance?.DebugOnlyGetState();
    }

    public static bool BelongsToCombatState(Creature? creature, CombatState? combatState)
    {
        return creature != null
               && combatState != null
               && ReferenceEquals(GetRawCombatState(creature), combatState);
    }

    public static bool IsPlayPhase(CombatState? combatState)
    {
        var phases = GetObservedPlayerTurnPhases(combatState);
        if (phases.Count > 0)
            return phases.All(phase => string.Equals(phase, "Play", StringComparison.Ordinal));

        return combatState != null
               && combatState.CurrentSide == CombatSide.Player
               && CombatManager.Instance?.IsPlayPhase == true;
    }

    public static bool IsPlayPhase(ICombatState? combatState)
    {
        return IsPlayPhase(GetCombatState(combatState));
    }

    public static string DescribePlayerTurnPhases(CombatState? combatState)
    {
        var phases = GetObservedPlayerTurnPhases(combatState);
        if (phases.Count > 0)
            return string.Join("/", phases.Distinct(StringComparer.Ordinal));

        if (combatState == null)
            return "unknown";

        return IsPlayPhase(combatState) ? "Play" : "NotPlay";
    }

    public static void RestorePlayPhaseIfNeeded(CombatManager? combatManager, bool? isPlayPhase)
    {
        if (combatManager == null || isPlayPhase != true)
            return;

        if (CombatManagerSetPhaseForAllPlayersMethod == null)
            return;

        try
        {
            var parameters = CombatManagerSetPhaseForAllPlayersMethod.GetParameters();
            if (parameters.Length != 1 || !parameters[0].ParameterType.IsEnum)
                return;

            var playPhase = Enum.Parse(parameters[0].ParameterType, "Play");
            CombatManagerSetPhaseForAllPlayersMethod.Invoke(combatManager, [playPhase]);
        }
        catch
        {
            // Older runtime variants do not expose a player-phase enum anymore.
            // In that case the restore path already uses direct CombatManager flags elsewhere.
        }
    }

    private static List<string> GetObservedPlayerTurnPhases(CombatState? combatState)
    {
        if (combatState == null || PlayerCombatStatePhaseProperty == null)
            return [];

        return combatState?.Players
                   .Select(player => player.PlayerCombatState == null
                       ? null
                       : PlayerCombatStatePhaseProperty.GetValue(player.PlayerCombatState)?.ToString())
                   .Where(phase => !string.IsNullOrWhiteSpace(phase))
                   .Cast<string>()
                   .ToList()
               ?? [];
    }
}
