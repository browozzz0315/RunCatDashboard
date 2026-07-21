using RunCatDashboard.App.Windowing;

namespace RunCatDashboard.App.Settings;

public static class AppSettingsValidator
{
    public static readonly IReadOnlySet<int> AllowedSamplingIntervals =
        new HashSet<int> { 250, 500, 1000, 2000, 5000 };

    public static AppSettings Normalize(AppSettings? settings)
    {
        AppSettings defaults = AppSettings.Defaults;
        if (settings is null)
        {
            return defaults;
        }

        double? left = settings.Window?.Left;
        double? top = settings.Window?.Top;
        if (!IsFinite(left) || !IsFinite(top) || left.HasValue != top.HasValue)
        {
            left = null;
            top = null;
        }

        OverlayInteractionMode mode = settings.Overlay is not null &&
            Enum.IsDefined(settings.Overlay.InteractionMode)
                ? settings.Overlay.InteractionMode
                : defaults.Overlay.InteractionMode;
        int interval = settings.Metrics is not null &&
            AllowedSamplingIntervals.Contains(settings.Metrics.SamplingIntervalMilliseconds)
                ? settings.Metrics.SamplingIntervalMilliseconds
                : defaults.Metrics.SamplingIntervalMilliseconds;

        return new AppSettings(
            AppSettings.CurrentVersion,
            new WindowSettings(left, top, settings.Window?.IsDashboardVisible ?? true),
            new OverlaySettings(mode),
            new MetricsSettings(interval),
            new StartupSettings(settings.Startup?.RunAtLoginRequested ?? false));
    }

    private static bool IsFinite(double? value) =>
        value is null || double.IsFinite(value.Value);
}
