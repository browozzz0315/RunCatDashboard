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
        OverlayHotKeyGesture hotKey = settings.Overlay?.InteractionHotKey is { } candidate &&
            candidate.TryValidate(out _)
                ? candidate
                : OverlayHotKeyGesture.Default;
        OverlayHotKeyGesture visibilityHotKey = settings.Window?.VisibilityHotKey is { } visibilityCandidate &&
            visibilityCandidate.TryValidate(out _)
                ? visibilityCandidate
                : OverlayHotKeyGesture.DashboardVisibilityDefault;
        if (visibilityHotKey == hotKey)
        {
            visibilityHotKey = OverlayHotKeyGesture.DashboardVisibilityDefault;
            if (visibilityHotKey == hotKey)
            {
                hotKey = OverlayHotKeyGesture.Default;
            }
        }
        OverlaySizeMode sizeMode = settings.Overlay is not null &&
            Enum.IsDefined(settings.Overlay.SizeMode)
                ? settings.Overlay.SizeMode
                : OverlaySizeMode.Standard;
        OverlayFieldSettings fields = NormalizeFields(
            sizeMode,
            settings.Overlay?.Fields);
        int interval = settings.Metrics is not null &&
            AllowedSamplingIntervals.Contains(settings.Metrics.SamplingIntervalMilliseconds)
                ? settings.Metrics.SamplingIntervalMilliseconds
                : defaults.Metrics.SamplingIntervalMilliseconds;
        ThemePreference themePreference = settings.Appearance is not null &&
            Enum.IsDefined(settings.Appearance.ThemePreference)
                ? settings.Appearance.ThemePreference
                : ThemePreference.System;

        return new AppSettings(
            AppSettings.CurrentVersion,
            new WindowSettings(
                left,
                top,
                settings.Window?.IsDashboardVisible ?? true,
                visibilityHotKey),
            new OverlaySettings(mode, hotKey, sizeMode, fields),
            new MetricsSettings(interval),
            new StartupSettings(settings.Startup?.RunAtLoginRequested ?? false))
        {
            Appearance = new AppearanceSettings(themePreference)
        };
    }

    private static bool IsFinite(double? value) =>
        value is null || double.IsFinite(value.Value);

    private static OverlayFieldSettings NormalizeFields(
        OverlaySizeMode mode,
        OverlayFieldSettings? fields)
    {
        if (mode == OverlaySizeMode.CatOnly)
        {
            return OverlayFieldSettings.ForMode(OverlaySizeMode.CatOnly);
        }

        if (fields is null || !fields.ShowCpu && !fields.ShowMemory)
        {
            return OverlayFieldSettings.ForMode(mode);
        }

        return fields;
    }

    public static bool TryValidatePresentation(
        OverlaySizeMode mode,
        OverlayFieldSettings fields,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(fields);
        if (!Enum.IsDefined(mode))
        {
            error = "Overlay 尺寸模式無效。";
            return false;
        }

        if (mode != OverlaySizeMode.CatOnly && !fields.ShowCpu && !fields.ShowMemory)
        {
            error = "Compact、Standard 與 Expanded 至少需要顯示 CPU 或 Memory。";
            return false;
        }

        error = null;
        return true;
    }
}
