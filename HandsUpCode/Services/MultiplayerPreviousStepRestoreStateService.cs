namespace HandsUp.HandsUpCode.Services;

public static class MultiplayerPreviousStepRestoreStateService
{
    public enum RestoreMode
    {
        None,
        MidCombatSnapshot
    }

    private static int _restoreDepth;
    private static RestoreMode _restoreMode;

    public static bool IsRestoreInProgress => _restoreDepth > 0;
    public static RestoreMode CurrentRestoreMode => IsRestoreInProgress ? _restoreMode : RestoreMode.None;
    public static bool ShouldSuppressCombatStartHooks => CurrentRestoreMode == RestoreMode.MidCombatSnapshot;

    public static void BeginRestore(RestoreMode restoreMode = RestoreMode.None)
    {
        if (_restoreDepth == 0)
            _restoreMode = restoreMode;

        _restoreDepth++;
    }

    public static void EndRestore()
    {
        if (_restoreDepth > 0)
            _restoreDepth--;

        if (_restoreDepth == 0)
            _restoreMode = RestoreMode.None;
    }

    public static void Clear()
    {
        _restoreDepth = 0;
        _restoreMode = RestoreMode.None;
    }
}
