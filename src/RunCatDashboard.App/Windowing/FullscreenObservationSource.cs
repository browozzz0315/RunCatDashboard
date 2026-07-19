using System.ComponentModel;
using System.Globalization;
using RunCatDashboard.App.Interop;

namespace RunCatDashboard.App.Windowing;

internal interface IFullscreenObservationSource
{
    FullscreenObservation Observe(nint overlayWindowHandle);
}

internal readonly record struct MonitorSnapshot(nint Handle, PixelBounds Bounds);

internal interface INativeFullscreenApi
{
    nint GetForegroundWindow();

    bool IsWindowVisible(nint windowHandle);

    bool IsWindowMinimized(nint windowHandle);

    bool TryGetExtendedFrameBounds(
        nint windowHandle,
        out PixelBounds bounds,
        out int errorCode);

    bool TryGetWindowBounds(
        nint windowHandle,
        out PixelBounds bounds,
        out int errorCode);

    MonitorSnapshot GetMonitorForWindow(nint windowHandle);
}

internal sealed class FullscreenObservationSource : IFullscreenObservationSource
{
    private readonly INativeFullscreenApi _nativeApi;
    private readonly int _tolerancePixels;

    internal FullscreenObservationSource(
        INativeFullscreenApi nativeApi,
        int tolerancePixels = FullscreenGeometry.DefaultTolerancePixels)
    {
        ArgumentNullException.ThrowIfNull(nativeApi);
        ArgumentOutOfRangeException.ThrowIfNegative(tolerancePixels);
        _nativeApi = nativeApi;
        _tolerancePixels = tolerancePixels;
    }

    public FullscreenObservation Observe(nint overlayWindowHandle)
    {
        if (overlayWindowHandle == nint.Zero)
        {
            return Fault("The overlay HWND is unavailable.", "Overlay monitor unavailable");
        }

        MonitorSnapshot overlayMonitor;
        try
        {
            overlayMonitor = _nativeApi.GetMonitorForWindow(overlayWindowHandle);
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            return Fault(
                $"Resolving the overlay monitor failed: {exception.Message}",
                "Overlay monitor unavailable");
        }

        string overlayDiagnostic = $"Overlay monitor {overlayMonitor.Bounds}";
        nint foregroundWindow = _nativeApi.GetForegroundWindow();
        if (foregroundWindow == nint.Zero)
        {
            return Excluded("No foreground window", overlayDiagnostic);
        }

        if (foregroundWindow == overlayWindowHandle)
        {
            return Excluded("Foreground is the overlay window", overlayDiagnostic);
        }

        if (!_nativeApi.IsWindowVisible(foregroundWindow))
        {
            return Excluded("Foreground window is not visible", overlayDiagnostic);
        }

        if (_nativeApi.IsWindowMinimized(foregroundWindow))
        {
            return Excluded("Foreground window is minimized", overlayDiagnostic);
        }

        PixelBounds foregroundBounds;
        string boundsSource;
        if (_nativeApi.TryGetExtendedFrameBounds(
                foregroundWindow,
                out foregroundBounds,
                out int dwmError))
        {
            boundsSource = "DWM extended frame bounds";
        }
        else if (_nativeApi.TryGetWindowBounds(
                     foregroundWindow,
                     out foregroundBounds,
                     out int windowRectError))
        {
            boundsSource = "GetWindowRect fallback";
        }
        else
        {
            return new FullscreenObservation(
                false,
                false,
                "Foreground bounds unavailable",
                overlayDiagnostic,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Fullscreen detection failed: DWM extended frame bounds returned 0x{dwmError:X8}; GetWindowRect failed with Win32 error {windowRectError}."));
        }

        MonitorSnapshot foregroundMonitor;
        try
        {
            foregroundMonitor = _nativeApi.GetMonitorForWindow(foregroundWindow);
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            return new FullscreenObservation(
                false,
                false,
                $"Foreground bounds {foregroundBounds} from {boundsSource}",
                overlayDiagnostic,
                $"Resolving the foreground monitor failed: {exception.Message}");
        }

        bool fullscreen = FullscreenGeometry.CoversMonitor(
            foregroundBounds,
            foregroundMonitor.Bounds,
            _tolerancePixels);
        bool sameMonitor = foregroundMonitor.Handle == overlayMonitor.Handle;

        return new FullscreenObservation(
            fullscreen,
            sameMonitor,
            $"Foreground {foregroundBounds} on monitor {foregroundMonitor.Bounds} via {boundsSource}",
            overlayDiagnostic,
            null);
    }

    private static FullscreenObservation Excluded(
        string reason,
        string overlayDiagnostic)
    {
        return new FullscreenObservation(false, false, reason, overlayDiagnostic, null);
    }

    private static FullscreenObservation Fault(string fault, string overlayDiagnostic)
    {
        return new FullscreenObservation(
            false,
            false,
            "Foreground not evaluated",
            overlayDiagnostic,
            fault);
    }
}
