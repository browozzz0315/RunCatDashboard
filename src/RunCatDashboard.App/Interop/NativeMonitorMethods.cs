using System.Runtime.InteropServices;

namespace RunCatDashboard.App.Interop;

internal static class NativeMonitorMethods
{
    internal const uint MonitorDefaultToNearest = 0x00000002;

    [DllImport("user32.dll")]
    internal static extern nint MonitorFromWindow(nint windowHandle, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetMonitorInfo(nint monitorHandle, ref MonitorInfo monitorInfo);

    [DllImport("user32.dll")]
    internal static extern uint GetDpiForWindow(nint windowHandle);
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeRectangle
{
    internal int Left;
    internal int Top;
    internal int Right;
    internal int Bottom;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct MonitorInfo
{
    internal uint Size;
    internal NativeRectangle Monitor;
    internal NativeRectangle WorkArea;
    internal uint Flags;
}
