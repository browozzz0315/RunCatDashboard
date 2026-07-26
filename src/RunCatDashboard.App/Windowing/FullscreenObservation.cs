namespace RunCatDashboard.App.Windowing;

public sealed record FullscreenObservation(
    bool IsFullscreen,
    bool IsOnOverlayMonitor,
    string ForegroundDiagnostic,
    string OverlayMonitorDiagnostic,
    string? Fault)
{
    public string? FaultOperation { get; init; }

    public int? NativeErrorCode { get; init; }

    public int? HResultCode { get; init; }

    public static FullscreenObservation Pending { get; } = new(
        false,
        false,
        "Foreground not evaluated",
        "Overlay monitor not evaluated",
        null);
}
