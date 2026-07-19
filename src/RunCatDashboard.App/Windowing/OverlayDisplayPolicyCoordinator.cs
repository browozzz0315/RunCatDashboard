namespace RunCatDashboard.App.Windowing;

internal sealed class OverlayDisplayPolicyCoordinator
{
    private FullscreenObservation _observation = FullscreenObservation.Pending;

    internal OverlayDisplayPolicy RequestedPolicy { get; private set; } =
        OverlayDisplayPolicy.HideOverFullscreenApps;

    internal OverlayDisplayPolicyState State => Calculate(RequestedPolicy, _observation);

    internal OverlayDisplayPolicyState SetPolicy(OverlayDisplayPolicy policy)
    {
        if (!Enum.IsDefined(policy))
        {
            throw new ArgumentOutOfRangeException(nameof(policy), policy, "Unknown overlay display policy.");
        }

        RequestedPolicy = policy;
        return State;
    }

    internal OverlayDisplayPolicyState UpdateObservation(FullscreenObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        _observation = observation;
        return State;
    }

    internal static OverlayDisplayPolicyState Calculate(
        OverlayDisplayPolicy policy,
        FullscreenObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);

        bool mustFailVisible = observation.Fault is not null;
        bool shouldHide =
            policy == OverlayDisplayPolicy.HideOverFullscreenApps &&
            !mustFailVisible &&
            observation.IsFullscreen &&
            observation.IsOnOverlayMonitor;

        return new OverlayDisplayPolicyState(
            policy,
            !shouldHide,
            policy != OverlayDisplayPolicy.NeverTopmost,
            observation.IsFullscreen,
            observation.IsOnOverlayMonitor,
            observation.ForegroundDiagnostic,
            observation.OverlayMonitorDiagnostic,
            observation.Fault);
    }
}
