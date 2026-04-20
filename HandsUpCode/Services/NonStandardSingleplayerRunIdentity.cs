using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace HandsUp.HandsUpCode.Services;

public static class NonStandardSingleplayerRunIdentity
{
    public enum ModeKind
    {
        Standard,
        Custom,
        Daily
    }

    public readonly record struct SnapshotScope(ModeKind Mode, string RunKey);

    public static bool ShouldUseIsolatedSnapshots(RunManager? runManager)
    {
        return TryCreateScope(runManager, out _);
    }

    public static bool TryCreateScope(RunManager? runManager, out SnapshotScope scope)
    {
        scope = default;
        if (runManager == null || !runManager.IsSinglePlayerOrFakeMultiplayer)
            return false;

        var snapshot = runManager.ToSave(null);
        return TryCreateScope(snapshot, out scope);
    }

    public static bool TryCreateScope(SerializableRun? snapshot, out SnapshotScope scope)
    {
        scope = default;
        if (snapshot == null)
            return false;

        var mode = GetMode(snapshot);
        if (mode == ModeKind.Standard)
            return false;

        scope = new SnapshotScope(mode, BuildRunKey(snapshot, mode));
        return true;
    }

    public static ModeKind GetMode(SerializableRun snapshot)
    {
        if (snapshot.Modifiers.Count <= 0)
            return ModeKind.Standard;

        return snapshot.DailyTime != null ? ModeKind.Daily : ModeKind.Custom;
    }

    public static string GetModeDirectoryName(ModeKind mode)
    {
        return mode switch
        {
            ModeKind.Custom => "custom",
            ModeKind.Daily => "daily",
            _ => "standard"
        };
    }

    private static string BuildRunKey(SerializableRun snapshot, ModeKind mode)
    {
        var localPlayer = snapshot.Players.FirstOrDefault(player => player.NetId == 1UL) ?? snapshot.Players.FirstOrDefault();
        var characterId = localPlayer?.CharacterId?.ToString() ?? "unknown_character";
        var seed = snapshot.SerializableRng?.Seed ?? "unknown_seed";
        var dailyTime = snapshot.DailyTime?.ToUnixTimeSeconds().ToString() ?? "no_daily";
        var modifierFingerprint = BuildModifierFingerprint(snapshot);
        var rawKey = $"{GetModeDirectoryName(mode)}|{snapshot.StartTime}|{characterId}|{seed}|{dailyTime}|{modifierFingerprint}";
        var hashedKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawKey)))[..16];

        return $"{snapshot.StartTime:D16}_{SanitizeFilePart(characterId)}_{hashedKey}";
    }

    private static string BuildModifierFingerprint(SerializableRun snapshot)
    {
        if (snapshot.Modifiers.Count == 0)
            return "none";

        return string.Join("__", snapshot.Modifiers.Select(modifier => modifier.Id?.ToString() ?? "unknown_modifier"));
    }

    private static string SanitizeFilePart(string value)
    {
        var invalidChars = System.IO.Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray());
        return sanitized.Replace(':', '_');
    }
}
