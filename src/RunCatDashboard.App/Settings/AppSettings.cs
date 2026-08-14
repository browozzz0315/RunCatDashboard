using RunCatDashboard.App.Windowing;

namespace RunCatDashboard.App.Settings;

public sealed record AppSettings(
    int Version,
    WindowSettings Window,
    OverlaySettings Overlay,
    MetricsSettings Metrics,
    StartupSettings Startup)
{
    public const int CurrentVersion = 5;

    public AppearanceSettings Appearance { get; init; } = AppearanceSettings.Defaults;

    public static AppSettings Defaults { get; } = new(
        CurrentVersion,
        new WindowSettings(
            null,
            null,
            true,
            OverlayHotKeyGesture.DashboardVisibilityDefault),
        new OverlaySettings(
            OverlayInteractionMode.ClickThrough,
            OverlayHotKeyGesture.Default,
            OverlaySizeMode.Standard,
            OverlayFieldSettings.ForMode(OverlaySizeMode.Standard)),
        new MetricsSettings(1000),
        new StartupSettings(false))
    {
        Appearance = AppearanceSettings.Defaults
    };
}

public sealed record WindowSettings(
    double? Left,
    double? Top,
    bool IsDashboardVisible,
    OverlayHotKeyGesture? VisibilityHotKey = null);

public sealed record OverlaySettings(
    OverlayInteractionMode InteractionMode,
    OverlayHotKeyGesture? InteractionHotKey = null,
    OverlaySizeMode SizeMode = OverlaySizeMode.Standard,
    OverlayFieldSettings? Fields = null);

public sealed record MetricsSettings(int SamplingIntervalMilliseconds);

public sealed record StartupSettings(bool RunAtLoginRequested);
