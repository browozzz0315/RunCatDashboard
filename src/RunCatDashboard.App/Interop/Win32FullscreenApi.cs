using System.ComponentModel;
using System.Runtime.InteropServices;
using RunCatDashboard.App.Windowing;

namespace RunCatDashboard.App.Interop;

internal sealed class Win32FullscreenApi : INativeFullscreenApi
{
    public nint GetForegroundWindow() => NativeFullscreenMethods.GetForegroundWindow();

    public bool IsWindowVisible(nint windowHandle) =>
        NativeFullscreenMethods.IsWindowVisible(windowHandle);

    public bool IsWindowMinimized(nint windowHandle) =>
        NativeFullscreenMethods.IsIconic(windowHandle);

    public bool TryGetExtendedFrameBounds(
        nint windowHandle,
        out PixelBounds bounds,
        out int errorCode)
    {
        int result = NativeFullscreenMethods.DwmGetWindowAttribute(
            windowHandle,
            NativeFullscreenMethods.DwmWindowAttributeExtendedFrameBounds,
            out NativeRectangle rectangle,
            (uint)Marshal.SizeOf<NativeRectangle>());
        errorCode = result;
        bounds = ToPixelBounds(rectangle);
        return result >= 0;
    }

    public bool TryGetWindowBounds(
        nint windowHandle,
        out PixelBounds bounds,
        out int errorCode)
    {
        bool succeeded = NativeFullscreenMethods.GetWindowRect(
            windowHandle,
            out NativeRectangle rectangle);
        errorCode = succeeded ? 0 : Marshal.GetLastPInvokeError();
        bounds = ToPixelBounds(rectangle);
        return succeeded;
    }

    public MonitorSnapshot GetMonitorForWindow(nint windowHandle)
    {
        nint monitorHandle = NativeMonitorMethods.MonitorFromWindow(
            windowHandle,
            NativeFullscreenMethods.MonitorDefaultToNull);
        if (monitorHandle == nint.Zero)
        {
            throw new InvalidOperationException("MonitorFromWindow returned no monitor.");
        }

        var monitorInfo = new MonitorInfo
        {
            Size = (uint)Marshal.SizeOf<MonitorInfo>()
        };
        if (!NativeMonitorMethods.GetMonitorInfo(monitorHandle, ref monitorInfo))
        {
            throw new Win32Exception(
                Marshal.GetLastPInvokeError(),
                "GetMonitorInfo failed while reading the full monitor bounds.");
        }

        return new MonitorSnapshot(
            monitorHandle,
            ToPixelBounds(monitorInfo.Monitor));
    }

    private static PixelBounds ToPixelBounds(NativeRectangle rectangle)
    {
        return new PixelBounds(
            rectangle.Left,
            rectangle.Top,
            rectangle.Right,
            rectangle.Bottom);
    }
}
