using System.Collections.Generic;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;

namespace HandsUp.HandsUpCode.Services;

public static class MultiplayerPreviousStepSpecialSnapshotRegistry
{
    private static readonly Dictionary<string, string[]> MonsterPrivateFields = new()
    {
        ["AXEBOT"] = ["_shouldPlaySpawnAnimation", "_stockOverrideAmount"],
        ["BOWLBUG_ROCK"] = ["_isOffBalance"],
        ["CEREMONIAL_BEAST"] = ["_isStunnedByPlowRemoval", "_inMidCharge"],
        ["CHOMPER"] = ["_screamFirst"],
        ["CORPSE_SLUG"] = ["_isRavenous", "_starterMoveIdx"],
        ["CROSSBOW_RUBY_RAIDER"] = ["_isCrossbowReloaded"],
        ["DECIMILLIPEDE_SEGMENT"] = ["_starterMoveIdx"],
        ["DOORMAKER"] = ["_originalHp", "_isPortalOpen"],
        ["FAT_GREMLIN"] = ["_isAwake"],
        ["FROG_KNIGHT"] = ["_hasBeetleCharged"],
        ["FUZZY_WURM_CRAWLER"] = ["_isPuffed"],
        ["GAS_BOMB"] = ["_hasExploded"],
        ["GREMLIN_MERC"] = ["_hasSpoken"],
        ["INKLET"] = ["_middleInklet"],
        ["KIN_FOLLOWER"] = ["_startsWithDance"],
        ["KIN_PRIEST"] = ["_speechUsed"],
        ["KNOWLEDGE_DEMON"] = ["_curseOfKnowledgeCounter", "_isBurnt"],
        ["LAGAVULIN_MATRIARCH"] = ["_isAwake"],
        ["LIVING_FOG"] = ["_bloatAmount"],
        ["LOUSE_PROGENITOR"] = ["_curled"],
        ["MECHA_KNIGHT"] = ["_isWoundUp"],
        ["NIBBIT"] = ["_isFront", "_isAlone"],
        ["OWL_MAGISTRATE"] = ["_isFlying"],
        ["PHANTASMAL_GARDENER"] = ["_enlargeTriggers"],
        ["PUNCH_CONSTRUCT"] = ["_startsWithStrongPunch", "_startingHpReduction"],
        ["QUEEN"] = ["_hasAmalgamDied"],
        ["SCROLL_OF_BITING"] = ["_starterMoveIdx"],
        ["SLUMBERING_BEETLE"] = ["_isAwake"],
        ["SNAPPING_JAXFRUIT"] = ["_isCharged"],
        ["SNEAKY_GREMLIN"] = ["_isAwake"],
        ["SOUL_FYSH"] = ["_isInvisible"],
        ["SPINY_TOAD"] = ["_isSpiny"],
        ["TEST_SUBJECT"] = ["_respawns", "_extraMultiClawCount"],
        ["THE_INSATIABLE"] = ["_hasLiquified"],
        ["THE_OBSCURA"] = ["_hasSummoned"],
        ["THIEVING_HOPPER"] = ["_isHovering"],
        ["TOUGH_EGG"] = ["_hatched", "_isHatched"],
        ["TWO_TAILED_RAT"] = ["_starterMoveIndex", "_turnsUntilSummonable", "_callForBackupCount"],
        ["WATERFALL_GIANT"] = ["_currentPressureGunDamage", "_steamEruptionDamage", "_isAboutToBlow", "_pressureBuildupIdx"],
        ["WRIGGLER"] = ["_startStunned"]
    };

    private static readonly Dictionary<string, string[]> PowerPrivateFields = new()
    {
        [ModelDb.Power<AdaptablePower>().Id.Entry] = ["_internalData.isReviving"],
        [ModelDb.Power<AutomationPower>().Id.Entry] = ["_internalData.cardsLeft"],
        [ModelDb.Power<BeaconOfHopePower>().Id.Entry] = ["_hasAlreadyBeenGivenBlock"],
        [ModelDb.Power<ChainsOfBindingPower>().Id.Entry] = ["_internalData.boundCardPlayed"],
        [ModelDb.Power<DarkEmbracePower>().Id.Entry] = ["_internalData.etherealCount"],
        [ModelDb.Power<FeralPower>().Id.Entry] = ["_internalData.zeroCostAttacksPlayed"],
        [ModelDb.Power<HardenedShellPower>().Id.Entry] = ["_internalData.damageReceivedThisTurn"],
        [ModelDb.Power<JugglingPower>().Id.Entry] = ["_internalData.attacksPlayedThisTurn"],
        [ModelDb.Power<NemesisPower>().Id.Entry] = ["_shouldApplyIntangible"],
        [ModelDb.Power<OrbitPower>().Id.Entry] = ["_internalData.energySpent", "_internalData.triggerCount"],
        [ModelDb.Power<OutbreakPower>().Id.Entry] = ["_internalData.timesPoisoned"],
        [ModelDb.Power<PanachePower>().Id.Entry] = ["_internalData.alreadyApplied"],
        [ModelDb.Power<ReattachPower>().Id.Entry] = ["_internalData.isReviving"],
        [ModelDb.Power<RitualPower>().Id.Entry] = ["_wasJustAppliedByEnemy"],
        ["SANDPIT_POWER"] = ["_initialAmount", "_initialTargetPosition"],
        [ModelDb.Power<SkittishPower>().Id.Entry] = ["_internalData.hasGainedBlockThisTurn"],
        [ModelDb.Power<SlothPower>().Id.Entry] = ["_cardsPlayedThisTurn"],
        ["SURROUNDED_POWER"] = ["_facing"],
        [ModelDb.Power<VoidFormPower>().Id.Entry] = ["_internalData.cardsPlayedThisTurn"]
    };

    public static bool TryGetMonsterPrivateFields(string monsterIdEntry, out IReadOnlyList<string> fieldNames)
    {
        if (MonsterPrivateFields.TryGetValue(monsterIdEntry, out var fields))
        {
            fieldNames = fields;
            return true;
        }

        fieldNames = [];
        return false;
    }

    public static bool TryGetPowerPrivateFields(string powerIdEntry, out IReadOnlyList<string> fieldNames)
    {
        if (PowerPrivateFields.TryGetValue(powerIdEntry, out var fields))
        {
            fieldNames = fields;
            return true;
        }

        fieldNames = [];
        return false;
    }
}
