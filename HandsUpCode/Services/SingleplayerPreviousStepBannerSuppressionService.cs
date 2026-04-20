namespace HandsUp.HandsUpCode.Services;

public static class SingleplayerPreviousStepBannerSuppressionService
{
    private static bool _suppressNextCombatStartBanner;
    private static bool _suppressNextCombatStartWait;
    private static bool _suppressNextPlayerTurnStartWait;

    public static void SuppressNextCombatStartPresentation()
    {
        _suppressNextCombatStartBanner = true;
        _suppressNextCombatStartWait = true;
        _suppressNextPlayerTurnStartWait = true;
    }

    public static bool TryConsumeBannerSuppression()
    {
        if (!_suppressNextCombatStartBanner)
            return false;

        _suppressNextCombatStartBanner = false;
        return true;
    }

    public static bool TryConsumeWaitSuppression()
    {
        if (!_suppressNextCombatStartWait)
            return false;

        _suppressNextCombatStartWait = false;
        return true;
    }

    public static bool TryConsumePlayerTurnStartWaitSuppression()
    {
        if (!_suppressNextPlayerTurnStartWait)
            return false;

        _suppressNextPlayerTurnStartWait = false;
        return true;
    }

    public static void Clear()
    {
        _suppressNextCombatStartBanner = false;
        _suppressNextCombatStartWait = false;
        _suppressNextPlayerTurnStartWait = false;
    }
}
