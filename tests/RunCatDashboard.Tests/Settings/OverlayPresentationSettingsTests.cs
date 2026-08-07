using RunCatDashboard.App.Settings;

namespace RunCatDashboard.Tests.Settings;

public sealed class OverlayPresentationSettingsTests
{
    [Theory]
    [InlineData(OverlaySizeMode.CatOnly)]
    [InlineData(OverlaySizeMode.Compact)]
    [InlineData(OverlaySizeMode.Standard)]
    [InlineData(OverlaySizeMode.Expanded)]
    public void Mode_HasStableDefaultsAndProfile(OverlaySizeMode mode)
    {
        OverlayFieldSettings fields = OverlayFieldSettings.ForMode(mode);
        OverlaySizeProfile profile = OverlaySizeProfiles.Get(mode);

        Assert.True(double.IsFinite(profile.Width) && profile.Width > 0);
        Assert.True(double.IsFinite(profile.CatViewportWidth) && profile.CatViewportWidth > 0);
        Assert.True(double.IsFinite(profile.CatViewportHeight) && profile.CatViewportHeight > 0);
        Assert.True(double.IsFinite(profile.CatRenderSize) && profile.CatRenderSize > 0);
        Assert.True(double.IsFinite(profile.CatRenderOffsetX));
        Assert.True(double.IsFinite(profile.CatRenderOffsetY));
        Assert.True(profile.ContentPadding >= 0);
        Assert.True(profile.MaxHeight > profile.CatViewportHeight);
        if (mode == OverlaySizeMode.CatOnly)
        {
            Assert.Equal(new OverlayFieldSettings(
                false, false, false, false, false, false, false, false), fields);
        }
        else
        {
            Assert.True(fields.ShowCpu || fields.ShowMemory);
        }
    }

    [Theory]
    [InlineData(OverlaySizeMode.CatOnly, 196d, 162d, 124d, 162d, 0d, -16d, 8d, 360d)]
    [InlineData(OverlaySizeMode.Compact, 300d, 262d, 164d, 262d, 0d, -46d, 10d, 620d)]
    [InlineData(OverlaySizeMode.Standard, 308d, 268d, 152d, 268d, 0d, -55d, 11d, 720d)]
    [InlineData(OverlaySizeMode.Expanded, 500d, 450d, 280d, 450d, 0d, -80d, 16d, 800d)]
    public void Profile_KeepsWidthPaddingAndMaxHeightWithLandscapeCatViewport(
        OverlaySizeMode mode,
        double expectedWidth,
        double expectedCatViewportWidth,
        double expectedCatViewportHeight,
        double expectedCatRenderSize,
        double expectedCatRenderOffsetX,
        double expectedCatRenderOffsetY,
        double expectedContentPadding,
        double expectedMaxHeight)
    {
        OverlaySizeProfile profile = OverlaySizeProfiles.Get(mode);

        Assert.Equal(expectedWidth, profile.Width);
        Assert.Equal(expectedCatViewportWidth, profile.CatViewportWidth);
        Assert.Equal(expectedCatViewportHeight, profile.CatViewportHeight);
        Assert.Equal(expectedCatRenderSize, profile.CatRenderSize);
        Assert.Equal(expectedCatRenderOffsetX, profile.CatRenderOffsetX);
        Assert.Equal(expectedCatRenderOffsetY, profile.CatRenderOffsetY);
        Assert.Equal(expectedContentPadding, profile.ContentPadding);
        Assert.Equal(expectedMaxHeight, profile.MaxHeight);
        Assert.Equal(GetCatViewportWidth(profile), profile.CatViewportWidth);
        Assert.True(profile.CatViewportWidth > profile.CatViewportHeight);
    }

    [Theory]
    [InlineData(OverlaySizeMode.Compact)]
    [InlineData(OverlaySizeMode.Standard)]
    [InlineData(OverlaySizeMode.Expanded)]
    public void NonCatMode_RequiresCpuOrMemory(OverlaySizeMode mode)
    {
        var fields = new OverlayFieldSettings(
            false, false, true, true, true, true, true, true);

        bool valid = AppSettingsValidator.TryValidatePresentation(mode, fields, out string? error);

        Assert.False(valid);
        Assert.Contains("CPU 或 Memory", error);
    }

    [Fact]
    public void CatOnly_AllowsCpuAndMemoryOff()
    {
        OverlayFieldSettings fields = OverlayFieldSettings.ForMode(OverlaySizeMode.CatOnly);

        Assert.True(AppSettingsValidator.TryValidatePresentation(
            OverlaySizeMode.CatOnly, fields, out string? error));
        Assert.Null(error);
    }

    private static double GetCatViewportWidth(OverlaySizeProfile profile) =>
        profile.Width - 16d - 2d - (2d * profile.ContentPadding);
}
