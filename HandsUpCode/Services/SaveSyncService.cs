using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace HandsUp.HandsUpCode.Services;

public static class SaveSyncService
{
    private static readonly string[] ProfileDirectories =
    [
        "profile1",
        "profile2",
        "profile3"
    ];

    private const string SyncMarkerFileName = ".handsup_save_sync_done";

    public static string SyncBaseSavesToModded()
    {
        try
        {
            var steamRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SlayTheSpire2",
                "steam");

            if (!Directory.Exists(steamRoot))
                return "HandsUp save sync skipped: steam save root was not found.";

            var steamIdDirectories = Directory.GetDirectories(steamRoot);
            if (steamIdDirectories.Length == 0)
                return "HandsUp save sync skipped: no Steam save directories were found.";

            var sourceProfilesFound = 0;
            var targetSlotsFilled = 0;
            var targetSlotsSkipped = 0;
            var steamIdsChecked = 0;
            var steamIdsSkipped = 0;

            foreach (var steamIdDirectory in steamIdDirectories)
            {
                var moddedRoot = Path.Combine(steamIdDirectory, "modded");
                Directory.CreateDirectory(moddedRoot);
                steamIdsChecked++;

                if (HasSyncMarker(moddedRoot))
                {
                    steamIdsSkipped++;
                    continue;
                }

                var sourceProfiles = GetSourceProfilesInOrder(steamIdDirectory);
                var emptyTargetSlots = GetEmptyTargetSlotsInOrder(moddedRoot);

                sourceProfilesFound += sourceProfiles.Count;

                var pairCount = Math.Min(sourceProfiles.Count, emptyTargetSlots.Count);
                for (var i = 0; i < pairCount; i++)
                {
                    CopyWholeDirectory(sourceProfiles[i], emptyTargetSlots[i]);
                    targetSlotsFilled++;
                }

                targetSlotsSkipped += Math.Max(0, sourceProfiles.Count - pairCount);
                WriteSyncMarker(moddedRoot);
            }

            return
                $"HandsUp save sync finished: checked {steamIdsChecked} steam save roots, skipped {steamIdsSkipped} roots already synced, found {sourceProfilesFound} base save slots, filled {targetSlotsFilled} modded empty slots, skipped {targetSlotsSkipped} base slots because no modded slot was free.";
        }
        catch (Exception e)
        {
            return $"HandsUp save sync failed: {e.Message}";
        }
    }

    private static List<string> GetSourceProfilesInOrder(string steamIdDirectory)
    {
        var result = new List<string>();

        foreach (var profileDirectoryName in ProfileDirectories)
        {
            var sourceProfileDirectory = Path.Combine(steamIdDirectory, profileDirectoryName);
            if (!Directory.Exists(sourceProfileDirectory))
                continue;

            if (!HasVisibleSaveData(sourceProfileDirectory))
                continue;

            result.Add(sourceProfileDirectory);
        }

        return result;
    }

    private static List<string> GetEmptyTargetSlotsInOrder(string moddedRoot)
    {
        var result = new List<string>();

        foreach (var profileDirectoryName in ProfileDirectories)
        {
            var targetProfileDirectory = Path.Combine(moddedRoot, profileDirectoryName);
            if (!Directory.Exists(targetProfileDirectory))
            {
                Directory.CreateDirectory(targetProfileDirectory);
                result.Add(targetProfileDirectory);
                continue;
            }

            if (IsSlotEmpty(targetProfileDirectory))
                result.Add(targetProfileDirectory);
        }

        return result;
    }

    private static bool HasVisibleSaveData(string profileDirectory)
    {
        return Directory.EnumerateFiles(profileDirectory, "*", SearchOption.AllDirectories).Any();
    }

    private static bool IsSlotEmpty(string profileDirectory)
    {
        return !Directory.EnumerateFiles(profileDirectory, "*", SearchOption.AllDirectories).Any();
    }

    private static void CopyWholeDirectory(string sourceDirectory, string targetDirectory)
    {
        Directory.CreateDirectory(targetDirectory);

        foreach (var sourceChildDirectory in Directory.GetDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, sourceChildDirectory);
            Directory.CreateDirectory(Path.Combine(targetDirectory, relativePath));
        }

        foreach (var sourceFilePath in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, sourceFilePath);
            var targetFilePath = Path.Combine(targetDirectory, relativePath);
            var targetParent = Path.GetDirectoryName(targetFilePath);
            if (!string.IsNullOrEmpty(targetParent))
                Directory.CreateDirectory(targetParent);

            File.Copy(sourceFilePath, targetFilePath, false);
        }
    }

    private static bool HasSyncMarker(string moddedRoot)
    {
        return File.Exists(Path.Combine(moddedRoot, SyncMarkerFileName));
    }

    private static void WriteSyncMarker(string moddedRoot)
    {
        var markerPath = Path.Combine(moddedRoot, SyncMarkerFileName);
        File.WriteAllText(
            markerPath,
            $"HandsUp one-time save sync completed at {DateTime.UtcNow:O}",
            System.Text.Encoding.UTF8);
    }
}
