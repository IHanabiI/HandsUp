using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;

namespace HandsUp.HandsUpCode.Patches;

[HarmonyPatch(typeof(KillConsoleCmd), nameof(KillConsoleCmd.Process))]
public static class MultiplayerKillConsoleCmdPatch
{
    public static bool Prefix(Player? issuingPlayer, string[] args, ref CmdResult __result)
    {
        var netType = RunManager.Instance.NetService?.Type;
        if (netType != NetGameType.Host && netType != NetGameType.Client)
            return true;

        if (!CombatManager.Instance.IsInProgress)
        {
            __result = new CmdResult(false, "This doesn't appear to be a combat!");
            return false;
        }

        var targets = new List<Creature>();
        var enemies = CombatManager.Instance.DebugOnlyGetState()!.Enemies.ToList();

        if (args.Length == 0)
        {
            targets.Add(enemies[0]);
        }
        else if (args[0].Equals("all"))
        {
            targets.AddRange(enemies);
        }
        else
        {
            if (!int.TryParse(args[0], out var targetIndex))
            {
                __result = new CmdResult(false, $"Invalid argument '{args[0]}'. Use a target index or 'all'.");
                return false;
            }

            if (targetIndex < 0 || targetIndex >= enemies.Count)
            {
                __result = new CmdResult(false, $"Invalid target index {targetIndex}. Valid range: 0-{enemies.Count - 1}");
                return false;
            }

            targets.Add(enemies[targetIndex]);
        }

        var killedIds = targets
            .Select(creature => creature.Monster)
            .Where(monster => monster != null)
            .Select(monster => monster!.Id.Entry.ToString());

        __result = new CmdResult(
            KillTargetsAndCheckWinConditionAsync(targets),
            true,
            $"Killed: [{string.Join(",", killedIds)}]");
        return false;
    }

    private static async Task KillTargetsAndCheckWinConditionAsync(List<Creature> targets)
    {
        foreach (var creature in targets)
            await CreatureCmd.Kill(creature, false);

        await CombatManager.Instance.CheckWinCondition();
    }
}
