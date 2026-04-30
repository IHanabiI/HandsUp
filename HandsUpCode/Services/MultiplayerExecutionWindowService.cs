using System;
using Godot;

namespace HandsUp.HandsUpCode.Services;

public static class MultiplayerExecutionWindowService
{
    private const ulong PostRestoreChecksumGraceMs = 2500;

    private static readonly object SyncRoot = new();

    private static int _activeExecutionWindows;
    private static int _criticalCombatSyncAllowanceWindows;
    private static ulong _suppressChecksumsUntilMsec;
    private static string _lastReason = string.Empty;

    public static bool ShouldSuppressIncomingChecksums
    {
        get
        {
            lock (SyncRoot)
                return ShouldSuppressIncomingChecksumsNoLock();
        }
    }

    public static bool HasActiveExecutionWindow
    {
        get
        {
            lock (SyncRoot)
                return _activeExecutionWindows > 0;
        }
    }

    public static bool ShouldAllowCriticalCombatSyncMessages
    {
        get
        {
            lock (SyncRoot)
                return _criticalCombatSyncAllowanceWindows > 0;
        }
    }

    public static void BeginExecutionWindow(string reason)
    {
        lock (SyncRoot)
        {
            _activeExecutionWindows++;
            _lastReason = reason;
            MainFile.Logger.Info(
                $"Armed multiplayer execute suppression window for {reason}. {DescribeCurrentStateNoLock()}");
        }
    }

    public static void CancelPendingExecutionWindow(string reason)
    {
        lock (SyncRoot)
        {
            if (_activeExecutionWindows > 0)
                _activeExecutionWindows--;

            _lastReason = reason;
            MainFile.Logger.Info(
                $"Canceled multiplayer execute suppression window for {reason}. {DescribeCurrentStateNoLock()}");
        }
    }

    public static void CompleteExecutionWindow(string reason)
    {
        lock (SyncRoot)
        {
            if (_activeExecutionWindows <= 0)
            {
                _lastReason = reason;
                MainFile.Logger.Info(
                    $"Skipped completing multiplayer execute suppression window for {reason} because it had already transitioned out of action suppression. {DescribeCurrentStateNoLock()}");
                return;
            }

            TransitionToChecksumGraceNoLock(reason, "Completed");
        }
    }

    public static void ReleaseActionSuppression(string reason)
    {
        lock (SyncRoot)
        {
            if (_activeExecutionWindows <= 0)
            {
                _lastReason = reason;
                MainFile.Logger.Info(
                    $"Skipped releasing multiplayer execute suppression window for {reason} because it had already transitioned out of action suppression. {DescribeCurrentStateNoLock()}");
                return;
            }

            TransitionToChecksumGraceNoLock(reason, "Released");
        }
    }

    public static void BeginCriticalCombatSyncAllowance(string reason)
    {
        lock (SyncRoot)
        {
            _criticalCombatSyncAllowanceWindows++;
            _lastReason = reason;
            MainFile.Logger.Info(
                $"Enabled multiplayer critical combat-sync allowance for {reason}. {DescribeCurrentStateNoLock()}");
        }
    }

    public static void EndCriticalCombatSyncAllowance(string reason)
    {
        lock (SyncRoot)
        {
            if (_criticalCombatSyncAllowanceWindows > 0)
                _criticalCombatSyncAllowanceWindows--;

            _lastReason = reason;
            MainFile.Logger.Info(
                $"Disabled multiplayer critical combat-sync allowance for {reason}. {DescribeCurrentStateNoLock()}");
        }
    }

    public static void Clear(string reason)
    {
        lock (SyncRoot)
        {
            _activeExecutionWindows = 0;
            _criticalCombatSyncAllowanceWindows = 0;
            _suppressChecksumsUntilMsec = 0;
            _lastReason = reason;
            MainFile.Logger.Info(
                $"Cleared multiplayer execute suppression window. {DescribeCurrentStateNoLock()}");
        }
    }

    public static string DescribeCurrentState()
    {
        lock (SyncRoot)
            return DescribeCurrentStateNoLock();
    }

    private static bool ShouldSuppressIncomingChecksumsNoLock()
    {
        return _activeExecutionWindows > 0 || Time.GetTicksMsec() < _suppressChecksumsUntilMsec;
    }

    private static void TransitionToChecksumGraceNoLock(string reason, string logPrefix)
    {
        _activeExecutionWindows--;
        _suppressChecksumsUntilMsec = Math.Max(
            _suppressChecksumsUntilMsec,
            Time.GetTicksMsec() + PostRestoreChecksumGraceMs);
        _lastReason = reason;
        MainFile.Logger.Info(
            $"{logPrefix} multiplayer execute suppression window for {reason}. {DescribeCurrentStateNoLock()}");
    }

    private static string DescribeCurrentStateNoLock()
    {
        var now = Time.GetTicksMsec();
        var graceRemainingMs = _suppressChecksumsUntilMsec > now
            ? _suppressChecksumsUntilMsec - now
            : 0;
        return $"active={_activeExecutionWindows} allowCombatSync={_criticalCombatSyncAllowanceWindows} suppressing={ShouldSuppressIncomingChecksumsNoLock()} graceRemainingMs={graceRemainingMs} lastReason={_lastReason}";
    }
}
