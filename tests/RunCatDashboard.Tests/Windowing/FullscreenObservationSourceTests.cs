using RunCatDashboard.App.Windowing;

namespace RunCatDashboard.Tests.Windowing;

public sealed class FullscreenObservationSourceTests
{
    private static readonly nint OverlayWindow = new(100);
    private static readonly nint ForegroundWindow = new(200);
    private static readonly MonitorSnapshot PrimaryMonitor = new(
        new nint(10),
        new PixelBounds(0, 0, 1920, 1080));

    [Fact]
    public void Observe_WhenDwmSucceeds_PrefersExtendedFrameBounds()
    {
        var native = CreateNative();
        native.DwmBounds = PrimaryMonitor.Bounds;
        native.WindowBounds = new PixelBounds(100, 100, 900, 700);

        FullscreenObservation result = CreateSource(native).Observe(OverlayWindow);

        Assert.True(result.IsFullscreen);
        Assert.Equal(1, native.DwmReadCount);
        Assert.Equal(0, native.WindowRectReadCount);
        Assert.Contains("DWM extended frame bounds", result.ForegroundDiagnostic);
    }

    [Fact]
    public void Observe_WhenDwmFails_FallsBackToGetWindowRect()
    {
        var native = CreateNative();
        native.DwmSucceeds = false;
        native.DwmError = unchecked((int)0x80070005);
        native.WindowBounds = PrimaryMonitor.Bounds;

        FullscreenObservation result = CreateSource(native).Observe(OverlayWindow);

        Assert.True(result.IsFullscreen);
        Assert.Null(result.Fault);
        Assert.Equal(1, native.WindowRectReadCount);
        Assert.Contains("GetWindowRect fallback", result.ForegroundDiagnostic);
    }

    [Fact]
    public void Observe_WhenBothBoundsReadsFail_ReturnsFaultAndNonFullscreen()
    {
        var native = CreateNative();
        native.DwmSucceeds = false;
        native.DwmError = unchecked((int)0x80004005);
        native.WindowRectSucceeds = false;
        native.WindowRectError = 1400;

        FullscreenObservation result = CreateSource(native).Observe(OverlayWindow);

        Assert.False(result.IsFullscreen);
        Assert.False(result.IsOnOverlayMonitor);
        Assert.Contains("DWM", result.Fault);
        Assert.Contains("1400", result.Fault);
    }

    [Fact]
    public void Observe_WhenForegroundIsShellWindow_ExcludesBeforeGeometry()
    {
        var native = CreateNative();
        native.ShellWindow = ForegroundWindow;

        FullscreenObservation result = CreateSource(native).Observe(OverlayWindow);

        Assert.False(result.IsFullscreen);
        Assert.Null(result.Fault);
        Assert.Contains("shell window", result.ForegroundDiagnostic);
        Assert.Equal(0, native.ClassNameReadCount);
        Assert.Equal(0, native.DwmReadCount);
    }

    [Theory]
    [InlineData("Progman")]
    [InlineData("WorkerW")]
    public void Observe_WhenForegroundClassIsDesktopShell_ExcludesBeforeGeometry(
        string className)
    {
        var native = CreateNative();
        native.ClassName = className;

        FullscreenObservation result = CreateSource(native).Observe(OverlayWindow);

        Assert.False(result.IsFullscreen);
        Assert.Null(result.Fault);
        Assert.Contains(className, result.ForegroundDiagnostic);
        Assert.Equal(0, native.DwmReadCount);
    }

    [Theory]
    [InlineData("CabinetWClass")]
    [InlineData("Chrome_WidgetWin_1")]
    public void Observe_WithNonDesktopFullscreenClass_StillUsesGeometry(
        string className)
    {
        var native = CreateNative();
        native.ClassName = className;

        FullscreenObservation result = CreateSource(native).Observe(OverlayWindow);

        Assert.True(result.IsFullscreen);
        Assert.Null(result.Fault);
        Assert.Contains(className, result.ForegroundDiagnostic);
        Assert.Equal(1, native.DwmReadCount);
    }

    [Fact]
    public void Observe_WhenClassNameReadFails_IsDiagnosticAndFailVisible()
    {
        var native = CreateNative();
        native.ClassNameReadSucceeds = false;
        native.ClassNameError = 1400;

        FullscreenObservation result = CreateSource(native).Observe(OverlayWindow);

        Assert.False(result.IsFullscreen);
        Assert.False(result.IsOnOverlayMonitor);
        Assert.Contains("class name", result.Fault);
        Assert.Contains("1400", result.Fault);
        Assert.Equal(0, native.DwmReadCount);
    }

    [Fact]
    public void Observe_WithFullscreenOnDifferentMonitor_ReportsDifferentMonitor()
    {
        var native = CreateNative();
        var secondaryMonitor = new MonitorSnapshot(
            new nint(20),
            new PixelBounds(-2560, 0, 0, 1440));
        native.Monitors[ForegroundWindow] = secondaryMonitor;
        native.DwmBounds = secondaryMonitor.Bounds;

        FullscreenObservation result = CreateSource(native).Observe(OverlayWindow);

        Assert.True(result.IsFullscreen);
        Assert.False(result.IsOnOverlayMonitor);
        Assert.Contains("[-2560,0]-[0,1440]", result.ForegroundDiagnostic);
    }

    [Theory]
    [InlineData(ForegroundEligibility.NoWindow, "No foreground window")]
    [InlineData(ForegroundEligibility.OverlayItself, "overlay window")]
    [InlineData(ForegroundEligibility.Invisible, "not visible")]
    [InlineData(ForegroundEligibility.Minimized, "minimized")]
    public void Observe_WithIneligibleForeground_DoesNotDetectFullscreen(
        ForegroundEligibility eligibility,
        string diagnostic)
    {
        var native = CreateNative();
        switch (eligibility)
        {
            case ForegroundEligibility.NoWindow:
                native.ForegroundWindow = nint.Zero;
                break;
            case ForegroundEligibility.OverlayItself:
                native.ForegroundWindow = OverlayWindow;
                break;
            case ForegroundEligibility.Invisible:
                native.IsVisible = false;
                break;
            case ForegroundEligibility.Minimized:
                native.IsMinimized = true;
                break;
        }

        FullscreenObservation result = CreateSource(native).Observe(OverlayWindow);

        Assert.False(result.IsFullscreen);
        Assert.Null(result.Fault);
        Assert.Contains(diagnostic, result.ForegroundDiagnostic, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, native.DwmReadCount);
    }

    private static FullscreenObservationSource CreateSource(FakeNativeFullscreenApi native) =>
        new(native);

    private static FakeNativeFullscreenApi CreateNative()
    {
        var native = new FakeNativeFullscreenApi
        {
            ForegroundWindow = ForegroundWindow,
            DwmBounds = PrimaryMonitor.Bounds,
            WindowBounds = PrimaryMonitor.Bounds
        };
        native.Monitors[OverlayWindow] = PrimaryMonitor;
        native.Monitors[ForegroundWindow] = PrimaryMonitor;
        return native;
    }

    public enum ForegroundEligibility
    {
        NoWindow,
        OverlayItself,
        Invisible,
        Minimized
    }

    private sealed class FakeNativeFullscreenApi : INativeFullscreenApi
    {
        internal Dictionary<nint, MonitorSnapshot> Monitors { get; } = [];
        internal nint ForegroundWindow { get; set; }
        internal nint ShellWindow { get; set; }
        internal string ClassName { get; set; } = "Chrome_WidgetWin_1";
        internal bool ClassNameReadSucceeds { get; set; } = true;
        internal int ClassNameError { get; set; }
        internal int ClassNameReadCount { get; private set; }
        internal bool IsVisible { get; set; } = true;
        internal bool IsMinimized { get; set; }
        internal bool DwmSucceeds { get; set; } = true;
        internal int DwmError { get; set; }
        internal PixelBounds DwmBounds { get; set; }
        internal bool WindowRectSucceeds { get; set; } = true;
        internal int WindowRectError { get; set; }
        internal PixelBounds WindowBounds { get; set; }
        internal int DwmReadCount { get; private set; }
        internal int WindowRectReadCount { get; private set; }

        public nint GetForegroundWindow() => ForegroundWindow;

        public nint GetShellWindow() => ShellWindow;

        public bool TryGetWindowClassName(
            nint windowHandle,
            out string className,
            out int errorCode)
        {
            ClassNameReadCount++;
            className = ClassNameReadSucceeds ? ClassName : string.Empty;
            errorCode = ClassNameError;
            return ClassNameReadSucceeds;
        }

        public bool IsWindowVisible(nint windowHandle) => IsVisible;

        public bool IsWindowMinimized(nint windowHandle) => IsMinimized;

        public bool TryGetExtendedFrameBounds(
            nint windowHandle,
            out PixelBounds bounds,
            out int errorCode)
        {
            DwmReadCount++;
            bounds = DwmBounds;
            errorCode = DwmError;
            return DwmSucceeds;
        }

        public bool TryGetWindowBounds(
            nint windowHandle,
            out PixelBounds bounds,
            out int errorCode)
        {
            WindowRectReadCount++;
            bounds = WindowBounds;
            errorCode = WindowRectError;
            return WindowRectSucceeds;
        }

        public MonitorSnapshot GetMonitorForWindow(nint windowHandle) => Monitors[windowHandle];
    }
}
