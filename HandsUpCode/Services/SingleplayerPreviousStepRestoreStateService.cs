namespace HandsUp.HandsUpCode.Services;

public static class SingleplayerPreviousStepRestoreStateService
{
    private static int _restoreDepth;
    private static bool _playableReplaySession;

    public static bool IsRestoreInProgress => _restoreDepth > 0;
    public static bool IsPlayableReplaySession => _playableReplaySession;

    public static void BeginRestore()
    {
        _restoreDepth++;
        _playableReplaySession = false;
    }

    public static void EndRestore()
    {
        if (_restoreDepth > 0)
            _restoreDepth--;
    }

    public static void EnablePlayableReplaySession()
    {
        _playableReplaySession = true;
    }

    public static void ClearPlayableReplaySession()
    {
        _playableReplaySession = false;
    }

    public static void Clear()
    {
        _restoreDepth = 0;
        _playableReplaySession = false;
    }
}
