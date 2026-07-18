using System.ComponentModel;
using RunCatDashboard.App.Interop;
using RunCatDashboard.App.Windowing;

namespace RunCatDashboard.Tests.Windowing;

public sealed class OverlayWindowControllerTests
{
    private static readonly nint WindowHandle = new(1234);

    [Fact]
    public void Constructor_StartsInteractiveAndUninitialized()
    {
        var controller = new OverlayWindowController(new FakeNativeWindowStyleApi());

        Assert.Equal(OverlayInteractionMode.Interactive, controller.Mode);
        Assert.False(controller.IsInitialized);
    }

    [Fact]
    public void Initialize_AppliesInteractiveStylesWithoutRemovingUnrelatedBits()
    {
        const long unrelatedStyle = 0x00004000L;
        var nativeApi = new FakeNativeWindowStyleApi
        {
            CurrentStyle = unrelatedStyle |
                (long)ExtendedWindowStyle.Transparent |
                (long)ExtendedWindowStyle.NoActivate
        };
        var controller = new OverlayWindowController(nativeApi);

        controller.Initialize(WindowHandle);

        Assert.True(controller.IsInitialized);
        Assert.Equal(
            unrelatedStyle | (long)ExtendedWindowStyle.ToolWindow,
            nativeApi.CurrentStyle);
        Assert.Equal(1, nativeApi.SetCount);
        Assert.Equal(1, nativeApi.RefreshCount);
    }

    [Fact]
    public void SetMode_InteractiveToClickThrough_AddsInputStyles()
    {
        var nativeApi = CreateInitializedNativeApi();
        var controller = new OverlayWindowController(nativeApi);
        controller.Initialize(WindowHandle);

        bool changed = controller.SetMode(OverlayInteractionMode.ClickThrough);

        Assert.True(changed);
        Assert.Equal(OverlayInteractionMode.ClickThrough, controller.Mode);
        AssertStyleIsSet(nativeApi.CurrentStyle, ExtendedWindowStyle.ToolWindow);
        AssertStyleIsSet(nativeApi.CurrentStyle, ExtendedWindowStyle.Transparent);
        AssertStyleIsSet(nativeApi.CurrentStyle, ExtendedWindowStyle.NoActivate);
    }

    [Fact]
    public void SetMode_ClickThroughToInteractive_RemovesOnlyInputStyles()
    {
        const long unrelatedStyle = 0x00004000L;
        var nativeApi = new FakeNativeWindowStyleApi
        {
            CurrentStyle = unrelatedStyle | (long)ExtendedWindowStyle.ToolWindow
        };
        var controller = new OverlayWindowController(nativeApi);
        controller.Initialize(WindowHandle);
        controller.SetMode(OverlayInteractionMode.ClickThrough);

        bool changed = controller.SetMode(OverlayInteractionMode.Interactive);

        Assert.True(changed);
        Assert.Equal(OverlayInteractionMode.Interactive, controller.Mode);
        Assert.Equal(
            unrelatedStyle | (long)ExtendedWindowStyle.ToolWindow,
            nativeApi.CurrentStyle);
    }

    [Fact]
    public void SetMode_WithSameMode_IsIdempotentAndSkipsNativeCalls()
    {
        var nativeApi = CreateInitializedNativeApi();
        var controller = new OverlayWindowController(nativeApi);
        controller.Initialize(WindowHandle);
        nativeApi.ResetCounts();

        bool changed = controller.SetMode(OverlayInteractionMode.Interactive);

        Assert.False(changed);
        Assert.Equal(0, nativeApi.GetCount);
        Assert.Equal(0, nativeApi.SetCount);
        Assert.Equal(0, nativeApi.RefreshCount);
    }

    [Fact]
    public void SetMode_BeforeHandleInitialization_ThrowsClearException()
    {
        var controller = new OverlayWindowController(new FakeNativeWindowStyleApi());

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => controller.SetMode(OverlayInteractionMode.ClickThrough));

        Assert.Contains("has not been initialized", exception.Message);
    }

    [Fact]
    public void Initialize_WhenNativeCallFails_ThrowsOverlayExceptionAndKeepsHandleInvalid()
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
        Assert.IsType<Win32Exception>(exception.InnerException);
        Assert.False(controller.IsInitialized);
    }

    [Fact]
    public void SetMode_WhenFrameRefreshFails_RestoresPreviousStyle()
    {
        var nativeApi = CreateInitializedNativeApi();
        var controller = new OverlayWindowController(nativeApi);
        controller.Initialize(WindowHandle);
        nativeApi.RefreshException = new Win32Exception(5, "Refresh failed");

        OverlayWindowException exception = Assert.Throws<OverlayWindowException>(
            () => controller.SetMode(OverlayInteractionMode.ClickThrough));

        Assert.Contains("previous native style was restored", exception.Message);
        Assert.Equal((long)ExtendedWindowStyle.ToolWindow, nativeApi.CurrentStyle);
        Assert.Equal(OverlayInteractionMode.Interactive, controller.Mode);
        Assert.Equal(2, nativeApi.SetCount);
    }

    [Fact]
    public void SetMode_WhenNativeCallFails_DoesNotCommitRequestedMode()
    {
        var nativeApi = CreateInitializedNativeApi();
        var controller = new OverlayWindowController(nativeApi);
        controller.Initialize(WindowHandle);
        nativeApi.GetException = new Win32Exception(1400, "Invalid window handle");

        Assert.Throws<OverlayWindowException>(
            () => controller.SetMode(OverlayInteractionMode.ClickThrough));
        Assert.Equal(OverlayInteractionMode.Interactive, controller.Mode);
    }

    [Fact]
    public void Close_InvalidatesHandleAndPreventsFurtherNativeOperations()
    {
        var nativeApi = CreateInitializedNativeApi();
        var controller = new OverlayWindowController(nativeApi);
        controller.Initialize(WindowHandle);
        nativeApi.ResetCounts();

        controller.Close();
        controller.Close();

        Assert.False(controller.IsInitialized);
        Assert.Throws<ObjectDisposedException>(
            () => controller.SetMode(OverlayInteractionMode.ClickThrough));
        Assert.Equal(0, nativeApi.GetCount);
        Assert.Equal(0, nativeApi.SetCount);
        Assert.Equal(0, nativeApi.RefreshCount);
    }

    private static FakeNativeWindowStyleApi CreateInitializedNativeApi()
    {
        return new FakeNativeWindowStyleApi
        {
            CurrentStyle = (long)ExtendedWindowStyle.ToolWindow
        };
    }

    private static void AssertStyleIsSet(long style, ExtendedWindowStyle expected)
    {
        Assert.Equal((long)expected, style & (long)expected);
    }

    private sealed class FakeNativeWindowStyleApi : INativeWindowStyleApi
    {
        internal long CurrentStyle { get; set; }

        internal Win32Exception? GetException { get; set; }

        internal Win32Exception? RefreshException { get; set; }

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
            CurrentStyle = style;
        }

        public void RefreshFrame(nint windowHandle)
        {
            RefreshCount++;
            if (RefreshException is not null)
            {
                Win32Exception exception = RefreshException;
                RefreshException = null;
                throw exception;
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
