using System.ComponentModel;
using RunCatDashboard.App.Interop;
using RunCatDashboard.App.Windowing;

namespace RunCatDashboard.Tests.Windowing;

public sealed class OverlayWindowControllerTests
{
    private static readonly nint WindowHandle = new(1234);

    [Fact]
    public void Constructor_RequestsClickThroughAndHasNoAppliedModeBeforeHwnd()
    {
        var controller = new OverlayWindowController(new FakeNativeWindowStyleApi());

        Assert.Equal(OverlayInteractionMode.ClickThrough, controller.State.RequestedMode);
        Assert.Null(controller.State.AppliedMode);
        Assert.False(controller.State.IsInitialized);
        Assert.False(controller.State.IsFaulted);
        Assert.Null(controller.State.LastError);
    }

    [Fact]
    public void Initialize_AppliesClickThroughAndPreservesUnrelatedBits()
    {
        const long unrelatedStyle = 0x00004000L;
        var nativeApi = new FakeNativeWindowStyleApi
        {
            CurrentStyle = unrelatedStyle
        };
        var controller = new OverlayWindowController(nativeApi);

        controller.Initialize(WindowHandle);

        Assert.True(controller.IsInitialized);
        Assert.Equal(OverlayInteractionMode.ClickThrough, controller.State.AppliedMode);
        Assert.Equal(controller.State.RequestedMode, controller.State.AppliedMode);
        AssertStyleIsSet(nativeApi.CurrentStyle, ExtendedWindowStyle.ToolWindow);
        AssertStyleIsSet(nativeApi.CurrentStyle, ExtendedWindowStyle.Transparent);
        AssertStyleIsSet(nativeApi.CurrentStyle, ExtendedWindowStyle.NoActivate);
        AssertStyleIsSet(nativeApi.CurrentStyle, (ExtendedWindowStyle)unrelatedStyle);
        Assert.Equal(1, nativeApi.SetCount);
        Assert.Equal(1, nativeApi.RefreshCount);
    }

    [Fact]
    public void SetMode_ClickThroughToInteractive_RemovesOnlyManagedInputStyles()
    {
        const long unrelatedStyle = 0x00004000L;
        var nativeApi = new FakeNativeWindowStyleApi
        {
            CurrentStyle = unrelatedStyle
        };
        var controller = new OverlayWindowController(nativeApi);
        controller.Initialize(WindowHandle);

        bool changed = controller.SetMode(OverlayInteractionMode.Interactive);

        Assert.True(changed);
        Assert.Equal(OverlayInteractionMode.Interactive, controller.State.AppliedMode);
        Assert.Equal(
            unrelatedStyle | (long)ExtendedWindowStyle.ToolWindow,
            nativeApi.CurrentStyle);
    }

    [Fact]
    public void SetMode_InteractiveToClickThrough_AddsManagedInputStyles()
    {
        var nativeApi = new FakeNativeWindowStyleApi();
        var controller = new OverlayWindowController(nativeApi);
        controller.Initialize(WindowHandle);
        controller.SetMode(OverlayInteractionMode.Interactive);

        bool changed = controller.SetMode(OverlayInteractionMode.ClickThrough);

        Assert.True(changed);
        Assert.Equal(OverlayInteractionMode.ClickThrough, controller.State.AppliedMode);
        AssertStyleIsSet(nativeApi.CurrentStyle, ExtendedWindowStyle.ToolWindow);
        AssertStyleIsSet(nativeApi.CurrentStyle, ExtendedWindowStyle.Transparent);
        AssertStyleIsSet(nativeApi.CurrentStyle, ExtendedWindowStyle.NoActivate);
    }

    [Fact]
    public void SetMode_WithSameAppliedMode_IsIdempotentAndSkipsNativeCalls()
    {
        var nativeApi = new FakeNativeWindowStyleApi();
        var controller = new OverlayWindowController(nativeApi);
        controller.Initialize(WindowHandle);
        nativeApi.ResetCounts();

        bool changed = controller.SetMode(OverlayInteractionMode.ClickThrough);

        Assert.False(changed);
        Assert.Equal(0, nativeApi.GetCount);
        Assert.Equal(0, nativeApi.SetCount);
        Assert.Equal(0, nativeApi.RefreshCount);
    }

    [Fact]
    public void SetMode_WhenStyleSetFails_DoesNotUpdateAppliedMode()
    {
        var nativeApi = new FakeNativeWindowStyleApi();
        var controller = new OverlayWindowController(nativeApi);
        controller.Initialize(WindowHandle);
        nativeApi.ResetCounts();
        nativeApi.SetFailures.Add(1);

        OverlayWindowException exception = Assert.Throws<OverlayWindowException>(
            () => controller.SetMode(OverlayInteractionMode.Interactive));

        Assert.Contains("style update was not confirmed", exception.Message);
        Assert.Equal(OverlayInteractionMode.Interactive, controller.State.RequestedMode);
        Assert.Equal(OverlayInteractionMode.ClickThrough, controller.State.AppliedMode);
        Assert.False(controller.State.IsFaulted);
        Assert.NotNull(controller.State.LastError);
    }

    [Fact]
    public void SetMode_WhenFrameRefreshFails_RollsBackAndKeepsAppliedMode()
    {
        var nativeApi = new FakeNativeWindowStyleApi();
        var controller = new OverlayWindowController(nativeApi);
        controller.Initialize(WindowHandle);
        long clickThroughStyle = nativeApi.CurrentStyle;
        nativeApi.ResetCounts();
        nativeApi.RefreshFailures.Add(1);

        OverlayWindowException exception = Assert.Throws<OverlayWindowException>(
            () => controller.SetMode(OverlayInteractionMode.Interactive));

        Assert.Contains("previous native style was restored", exception.Message);
        Assert.Equal(clickThroughStyle, nativeApi.CurrentStyle);
        Assert.Equal(OverlayInteractionMode.ClickThrough, controller.State.AppliedMode);
        Assert.False(controller.State.IsFaulted);
        Assert.Equal(2, nativeApi.SetCount);
        Assert.Equal(2, nativeApi.RefreshCount);
    }

    [Fact]
    public void SetMode_WhenRollbackFails_EntersFaultStateAndRejectsFurtherOperations()
    {
        var nativeApi = new FakeNativeWindowStyleApi();
        var controller = new OverlayWindowController(nativeApi);
        controller.Initialize(WindowHandle);
        nativeApi.ResetCounts();
        nativeApi.RefreshFailures.Add(1);
        nativeApi.SetFailures.Add(2);

        OverlayWindowException exception = Assert.Throws<OverlayWindowException>(
            () => controller.SetMode(OverlayInteractionMode.Interactive));

        Assert.Contains("native window style is unknown", exception.Message);
        Assert.True(controller.State.IsFaulted);
        Assert.Null(controller.State.AppliedMode);
        Assert.NotNull(controller.State.LastError);

        int getCount = nativeApi.GetCount;
        Assert.Throws<OverlayWindowException>(
            () => controller.SetMode(OverlayInteractionMode.ClickThrough));
        Assert.Equal(getCount, nativeApi.GetCount);
    }

    [Fact]
    public void Initialize_WhenNativeReadFails_HasClearErrorAndKeepsHandleInvalid()
    {
        var nativeApi = new FakeNativeWindowStyleApi
        {
            GetException = new Win32Exception(5, "Access denied")
        };
        var controller = new OverlayWindowController(nativeApi);

        OverlayWindowException exception = Assert.Throws<OverlayWindowException>(
            () => controller.Initialize(WindowHandle));

        Assert.Contains("initialize overlay window styles", exception.Message);
        Assert.Contains("could not be read", exception.Message);
        Assert.False(controller.IsInitialized);
        Assert.Null(controller.State.AppliedMode);
        Assert.NotNull(controller.State.LastError);
    }

    [Fact]
    public void SetMode_BeforeHandleInitialization_ThrowsClearException()
    {
        var controller = new OverlayWindowController(new FakeNativeWindowStyleApi());

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => controller.SetMode(OverlayInteractionMode.Interactive));

        Assert.Contains("has not been initialized", exception.Message);
    }

    [Fact]
    public void Close_IsIdempotentAndPreventsFurtherNativeOperations()
    {
        var nativeApi = new FakeNativeWindowStyleApi();
        var controller = new OverlayWindowController(nativeApi);
        controller.Initialize(WindowHandle);
        nativeApi.ResetCounts();

        controller.Close();
        controller.Close();

        Assert.False(controller.IsInitialized);
        Assert.Null(controller.State.AppliedMode);
        Assert.Throws<ObjectDisposedException>(
            () => controller.SetMode(OverlayInteractionMode.Interactive));
        Assert.Equal(0, nativeApi.GetCount);
        Assert.Equal(0, nativeApi.SetCount);
        Assert.Equal(0, nativeApi.RefreshCount);
    }

    private static void AssertStyleIsSet(long style, ExtendedWindowStyle expected)
    {
        Assert.Equal((long)expected, style & (long)expected);
    }

    private sealed class FakeNativeWindowStyleApi : INativeWindowStyleApi
    {
        internal long CurrentStyle { get; set; }

        internal Win32Exception? GetException { get; set; }

        internal HashSet<int> SetFailures { get; } = [];

        internal HashSet<int> RefreshFailures { get; } = [];

        internal int GetCount { get; private set; }

        internal int SetCount { get; private set; }

        internal int RefreshCount { get; private set; }

        public long GetExtendedStyle(nint windowHandle)
        {
            GetCount++;
            if (GetException is not null)
            {
                throw GetException;
            }

            return CurrentStyle;
        }

        public void SetExtendedStyle(nint windowHandle, long style)
        {
            SetCount++;
            if (SetFailures.Contains(SetCount))
            {
                throw new Win32Exception(5, "Configured style set failure.");
            }

            CurrentStyle = style;
        }

        public void RefreshFrame(nint windowHandle)
        {
            RefreshCount++;
            if (RefreshFailures.Contains(RefreshCount))
            {
                throw new Win32Exception(5, "Configured frame refresh failure.");
            }
        }

        internal void ResetCounts()
        {
            GetCount = 0;
            SetCount = 0;
            RefreshCount = 0;
        }
    }
}
