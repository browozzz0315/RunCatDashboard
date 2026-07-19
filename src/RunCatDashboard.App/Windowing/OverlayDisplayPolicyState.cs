namespace RunCatDashboard.App.Windowing;

public sealed record OverlayDisplayPolicyState(
    OverlayDisplayPolicy RequestedPolicy,
    bool IsVisible,
    bool IsTopmost,
    bool IsFullscreenDetected,
    bool IsForegroundOnOverlayMonitor,
    string ForegroundDiagnostic,
    string OverlayMonitorDiagnostic,
    string? Fault);
