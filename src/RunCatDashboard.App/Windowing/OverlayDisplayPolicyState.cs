namespace RunCatDashboard.App.Windowing;

public sealed record OverlayDisplayPolicyState(
    OverlayDisplayPolicy RequestedPolicy,
    bool IsVisible,
    bool IsTopmost,
    bool IsFullscreenDetected,
    bool IsForegroundOnOverlayMonitor,
    string ForegroundDiagnostic,
    string OverlayMonitorDiagnostic,
    string? Fault)
{
    public string? FaultOperation { get; init; }

    public int? NativeErrorCode { get; init; }

    public int? HResultCode { get; init; }
}
