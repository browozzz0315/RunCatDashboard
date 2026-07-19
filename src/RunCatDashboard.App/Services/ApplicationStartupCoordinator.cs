namespace RunCatDashboard.App.Services;

internal enum ApplicationStartupDecision
{
    StartPrimaryInstance,
    ExitSecondaryInstance
}

internal sealed class ApplicationStartupCoordinator
{
    private readonly IApplicationInstanceGuard _instanceGuard;
    private bool _hasCoordinatedStartup;
    private ApplicationStartupDecision _decision;

    internal ApplicationStartupCoordinator(IApplicationInstanceGuard instanceGuard)
    {
        ArgumentNullException.ThrowIfNull(instanceGuard);
        _instanceGuard = instanceGuard;
    }

    internal ApplicationStartupDecision Coordinate(
        Action startPrimaryInstance,
        Action showSecondaryInstanceMessage)
    {
        ArgumentNullException.ThrowIfNull(startPrimaryInstance);
        ArgumentNullException.ThrowIfNull(showSecondaryInstanceMessage);

        if (_hasCoordinatedStartup)
        {
            return _decision;
        }

        if (_instanceGuard.TryAcquireOwnership())
        {
            _decision = ApplicationStartupDecision.StartPrimaryInstance;
            _hasCoordinatedStartup = true;
            startPrimaryInstance();
            return _decision;
        }

        _decision = ApplicationStartupDecision.ExitSecondaryInstance;
        _hasCoordinatedStartup = true;
        showSecondaryInstanceMessage();
        return _decision;
    }
}
