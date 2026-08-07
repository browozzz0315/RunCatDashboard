namespace RunCatDashboard.App.Settings;

public enum OverlaySizeMode
{
    CatOnly,
    Compact,
    Standard,
    Expanded
}

public sealed record OverlayFieldSettings(
    bool ShowCpu,
    bool ShowMemory,
    bool ShowUsedAndTotalMemory,
    bool ShowLastUpdated,
    bool ShowSamplingStatus,
    bool ShowRecentCpuHistory,
    bool ShowInteractionMode,
    bool ShowHotKeyHints)
{
    public static OverlayFieldSettings ForMode(OverlaySizeMode mode) => mode switch
    {
        OverlaySizeMode.CatOnly => new(false, false, false, false, false, false, false, false),
        OverlaySizeMode.Compact => new(true, true, false, false, false, false, false, false),
        OverlaySizeMode.Standard => new(true, true, true, true, true, false, true, false),
        OverlaySizeMode.Expanded => new(true, true, true, true, true, true, true, true),
        _ => ForMode(OverlaySizeMode.Standard)
    };
}

public sealed record OverlaySizeProfile(
    double Width,
    double CatViewportWidth,
    double CatViewportHeight,
    double CatRenderSize,
    double CatRenderOffsetX,
    double CatRenderOffsetY,
    double ContentPadding,
    double MaxHeight);

public static class OverlaySizeProfiles
{
    public static OverlaySizeProfile Get(OverlaySizeMode mode) => mode switch
    {
        OverlaySizeMode.CatOnly => new(196d, 162d, 124d, 162d, 0d, -16d, 8d, 360d),
        OverlaySizeMode.Compact => new(300d, 262d, 164d, 262d, 0d, -46d, 10d, 620d),
        OverlaySizeMode.Standard => new(308d, 268d, 152d, 268d, 0d, -55d, 11d, 720d),
        OverlaySizeMode.Expanded => new(500d, 450d, 280d, 450d, 0d, -80d, 16d, 800d),
        _ => Get(OverlaySizeMode.Standard)
    };
}
