using RunCatDashboard.App.Windowing;

namespace RunCatDashboard.App.Settings;

public sealed record AppSettings(
    int Version,
    WindowSettings Window,
    OverlaySettings Overlay,
    MetricsSettings Metrics,
    StartupSettings Startup)
{
    public const int CurrentVersion = 6;

    public AppearanceSettings Appearance { get; init; } = AppearanceSettings.Defaults;

    public AnimationSettings Animation { get; init; } = AnimationSettings.Defaults;

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
        Appearance = AppearanceSettings.Defaults,
        Animation = AnimationSettings.Defaults
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

public enum AnimationSpeedPreference
{
    Slow,
    Normal,
    Fast
}

public sealed record AnimationSettings(
    string? SelectedAnimationId,
    AnimationSpeedPreference SpeedPreference,
    int FormatVersion)
{
    public const int CurrentFormatVersion = 1;
    public const string BuiltInDefaultAnimationId = "builtin.cat2-run";

    public static AnimationSettings Defaults { get; } = new(
        BuiltInDefaultAnimationId,
        AnimationSpeedPreference.Normal,
        CurrentFormatVersion);
}
