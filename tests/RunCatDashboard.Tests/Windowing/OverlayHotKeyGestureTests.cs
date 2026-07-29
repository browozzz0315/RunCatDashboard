using RunCatDashboard.App.Windowing;

namespace RunCatDashboard.Tests.Windowing;

public sealed class OverlayHotKeyGestureTests
{
    [Fact]
    public void DisplayText_UsesModelModifiersAndFriendlyDigitName()
    {
        var gesture = new OverlayHotKeyGesture(
            true, false, true, true, OverlayHotKeyKey.D0);

        Assert.Equal("Ctrl + Shift + Win + 0", gesture.DisplayText);
        Assert.Equal("Ctrl + Alt + Shift + R", OverlayHotKeyGesture.Default.DisplayText);
    }

    [Fact]
    public void SupportedKeys_ContainsOnlyAZDigitsAndF1ToF12()
    {
        Assert.Equal(48, OverlayHotKeyGesture.SupportedKeys.Count);
        Assert.Contains(OverlayHotKeyKey.A, OverlayHotKeyGesture.SupportedKeys);
        Assert.Contains(OverlayHotKeyKey.D9, OverlayHotKeyGesture.SupportedKeys);
        Assert.Contains(OverlayHotKeyKey.F12, OverlayHotKeyGesture.SupportedKeys);
        Assert.DoesNotContain(OverlayHotKeyKey.Tab, OverlayHotKeyGesture.SupportedKeys);
        Assert.DoesNotContain(OverlayHotKeyKey.Escape, OverlayHotKeyGesture.SupportedKeys);
    }

    [Fact]
    public void NoModifier_IsRejected()
    {
        var gesture = new OverlayHotKeyGesture(
            false, false, false, false, OverlayHotKeyKey.A);

        Assert.False(gesture.TryValidate(out string? error));
        Assert.Contains("modifier", error);
    }

    [Fact]
    public void UnsupportedPrimaryKey_IsRejected()
    {
        var gesture = new OverlayHotKeyGesture(
            true, false, false, false, OverlayHotKeyKey.Tab);

        Assert.False(gesture.TryValidate(out string? error));
        Assert.Contains("A-Z、0-9 或 F1-F12", error);
    }

    [Fact]
    public void DashboardVisibilityGesture_IsRejectedWithSpecificMessage()
    {
        var gesture = new OverlayHotKeyGesture(
            true, true, true, false, OverlayHotKeyKey.D);

        Assert.False(gesture.TryValidate(out string? error));
        Assert.Equal(OverlayHotKeyGesture.DashboardVisibilityConflictMessage, error);
    }

    [Theory]
    [InlineData(OverlayHotKeyKey.S)]
    [InlineData(OverlayHotKeyKey.C)]
    [InlineData(OverlayHotKeyKey.V)]
    [InlineData(OverlayHotKeyKey.Z)]
    [InlineData(OverlayHotKeyKey.F)]
    [InlineData(OverlayHotKeyKey.P)]
    [InlineData(OverlayHotKeyKey.W)]
    public void CommonControlGesture_IsValidWithNonBlockingWarning(OverlayHotKeyKey key)
    {
        var gesture = new OverlayHotKeyGesture(true, false, false, false, key);

        Assert.True(gesture.TryValidate(out string? error));
        Assert.Null(error);
        Assert.Equal(OverlayHotKeyGesture.CommonApplicationGestureWarning, gesture.UsageWarning);
    }

    [Theory]
    [MemberData(nameof(BlockedSystemGestures))]
    public void ReservedSystemGesture_IsRejected(OverlayHotKeyGesture gesture)
    {
        Assert.False(gesture.TryValidate(out string? error));
        Assert.Contains("系統快捷鍵", error);
    }

    [Theory]
    [InlineData(OverlayHotKeyKey.A)]
    [InlineData(OverlayHotKeyKey.Z)]
    [InlineData(OverlayHotKeyKey.D0)]
    [InlineData(OverlayHotKeyKey.D9)]
    [InlineData(OverlayHotKeyKey.F1)]
    [InlineData(OverlayHotKeyKey.F12)]
    public void SupportedPrimaryKey_WithModifier_IsValid(OverlayHotKeyKey key)
    {
        var gesture = new OverlayHotKeyGesture(true, false, false, false, key);

        Assert.True(gesture.TryValidate(out string? error));
        Assert.Null(error);
    }

    public static TheoryData<OverlayHotKeyGesture> BlockedSystemGestures => new()
    {
        new(false, true, false, false, OverlayHotKeyKey.F4),
        new(false, true, false, false, OverlayHotKeyKey.Tab),
        new(true, false, false, false, OverlayHotKeyKey.Escape),
        new(false, false, false, true, OverlayHotKeyKey.D),
        new(false, false, false, true, OverlayHotKeyKey.E),
        new(false, false, false, true, OverlayHotKeyKey.I),
        new(false, false, false, true, OverlayHotKeyKey.L),
        new(false, false, false, true, OverlayHotKeyKey.R),
        new(false, false, false, true, OverlayHotKeyKey.Tab)
    };
}
