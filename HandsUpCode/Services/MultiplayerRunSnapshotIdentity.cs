using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

namespace HandsUp.HandsUpCode.Services;

public static class MultiplayerRunSnapshotIdentity
{
    public static bool TryCreateRunKey(RunManager? runManager, out string runKey)
    {
        runKey = string.Empty;
        var snapshot = runManager?.ToSave(null);
        return TryCreateRunKey(snapshot, out runKey);
    }

    public static bool TryCreateRunKey(SerializableRun? snapshot, out string runKey)
    {
        runKey = string.Empty;
        if (snapshot == null)
            return false;

        var seed = snapshot.SerializableRng?.Seed ?? "unknown_seed";
        var dailyTime = snapshot.DailyTime?.ToUnixTimeSeconds().ToString() ?? "no_daily";
        var modifiers = snapshot.Modifiers.Count == 0
            ? "none"
            : string.Join("__", snapshot.Modifiers.Select(modifier => modifier.Id?.ToString() ?? "unknown_modifier"));
        var orderedPlayers = snapshot.Players
            .OrderBy(player => player.NetId)
            .Select(player => $"{player.NetId}:{player.CharacterId?.ToString() ?? "unknown_character"}")
            .ToList();
        var playerFingerprint = orderedPlayers.Count == 0
            ? "no_players"
            : string.Join("__", orderedPlayers);
        var primaryCharacter = snapshot.Players
            .OrderBy(player => player.NetId)
            .Select(player => player.CharacterId?.ToString())
            .FirstOrDefault(characterId => !string.IsNullOrWhiteSpace(characterId))
            ?? "unknown_character";

        var rawKey = $"{snapshot.StartTime}|{seed}|{dailyTime}|{modifiers}|{playerFingerprint}";
        var hashedKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawKey)))[..16];
        runKey = $"{snapshot.StartTime:D16}_{SanitizeFilePart(primaryCharacter)}_{hashedKey}";
        return true;
    }

    private static string SanitizeFilePart(string value)
    {
        var invalidChars = System.IO.Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray());
        return sanitized.Replace(':', '_');
    }
}
