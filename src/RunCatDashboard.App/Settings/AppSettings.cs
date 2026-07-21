using RunCatDashboard.App.Windowing;

namespace RunCatDashboard.App.Settings;

public sealed record AppSettings(
    int Version,
    WindowSettings Window,
    OverlaySettings Overlay,
    MetricsSettings Metrics,
    StartupSettings Startup)
{
    public const int CurrentVersion = 1;

    public static AppSettings Defaults { get; } = new(
        CurrentVersion,
        new WindowSettings(null, null, true),
        new OverlaySettings(OverlayInteractionMode.ClickThrough),
        new MetricsSettings(1000),
        new StartupSettings(false));
}

public sealed record WindowSettings(
    double? Left,
    double? Top,
    bool IsDashboardVisible);

public sealed record OverlaySettings(OverlayInteractionMode InteractionMode);

public sealed record MetricsSettings(int SamplingIntervalMilliseconds);

public sealed record StartupSettings(bool RunAtLoginRequested);
