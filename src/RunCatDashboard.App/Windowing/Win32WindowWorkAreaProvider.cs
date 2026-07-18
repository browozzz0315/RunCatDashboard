using System.ComponentModel;
using System.Runtime.InteropServices;
using RunCatDashboard.App.Interop;

namespace RunCatDashboard.App.Windowing;

internal sealed class Win32WindowWorkAreaProvider : IWindowWorkAreaProvider
{
    private const double DefaultDpi = 96d;

    public WindowWorkArea GetForWindow(nint windowHandle)
    {
        if (windowHandle == nint.Zero)
        {
            throw new ArgumentException("A valid native window handle is required.", nameof(windowHandle));
        }

        nint monitorHandle = NativeMonitorMethods.MonitorFromWindow(
            windowHandle,
            NativeMonitorMethods.MonitorDefaultToNearest);
        if (monitorHandle == nint.Zero)
        {
            throw new InvalidOperationException("No monitor could be resolved for the overlay window.");
        }

        var monitorInfo = new MonitorInfo
        {
            Size = (uint)Marshal.SizeOf<MonitorInfo>()
        };
        if (!NativeMonitorMethods.GetMonitorInfo(monitorHandle, ref monitorInfo))
        {
            throw new Win32Exception(
                Marshal.GetLastPInvokeError(),
                "GetMonitorInfo failed for the overlay window.");
        }

        uint dpi = NativeMonitorMethods.GetDpiForWindow(windowHandle);
        double scale = (dpi == 0 ? DefaultDpi : dpi) / DefaultDpi;
        NativeRectangle area = monitorInfo.WorkArea;

        return new WindowWorkArea(
            area.Left / scale,
            area.Top / scale,
            (area.Right - area.Left) / scale,
            (area.Bottom - area.Top) / scale);
    }
}
