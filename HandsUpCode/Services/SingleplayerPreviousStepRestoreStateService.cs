namespace HandsUp.HandsUpCode.Services;

public static class SingleplayerPreviousStepRestoreStateService
{
    private static int _restoreDepth;

    public static bool IsRestoreInProgress => _restoreDepth > 0;

    public static void BeginRestore()
    {
        _restoreDepth++;
    }

    public static void EndRestore()
    {
        if (_restoreDepth > 0)
            _restoreDepth--;
    }

    public static void Clear()
    {
        _restoreDepth = 0;
    }
}
