namespace RunCatDashboard.App.Windowing;

public sealed record WindowVisibilityState(
    bool IsUserRequestedVisible,
    bool IsFullscreenPolicyVisible,
    bool IsActuallyVisible,
    bool IsExiting);
