using System.Collections.Generic;
using MegaCrit.Sts2.Core.Models;

namespace HandsUp.HandsUpCode.Services;

public static class SingleplayerPreviousStepSpecialSnapshotRegistry
{
    // High-risk monsters with hidden primitive fields that can affect future intent/state-machine behavior.
    private static readonly Dictionary<string, string[]> MonsterPrivateFields = new()
    {
        ["AXEBOT"] = ["_shouldPlaySpawnAnimation"],
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
        ["SANDPIT_POWER"] = ["_initialAmount", "_initialTargetPosition"],
        ["SURROUNDED_POWER"] = ["_facing"]
    };

    private static readonly Dictionary<string, string[]> CardPrivateFieldsByTypeName = new()
    {
        ["Claw"] = ["_extraDamageFromClawPlays"],
        ["KinglyPunch"] = ["_extraDamage"],
        ["Maul"] = ["_extraDamageFromMaulPlays"],
        ["Rampage"] = ["_extraDamageFromPlays"],
        ["SovereignBlade"] = ["_createdThroughForge", "_currentDamage", "_currentRepeats"],
        ["Thrash"] = ["_extraDamage"]
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

    public static bool TryGetCardPrivateFields(CardModel? card, out IReadOnlyList<string> fieldNames)
    {
        if (card != null && CardPrivateFieldsByTypeName.TryGetValue(card.GetType().Name, out var fields))
        {
            fieldNames = fields;
            return true;
        }

        fieldNames = [];
        return false;
    }
}
