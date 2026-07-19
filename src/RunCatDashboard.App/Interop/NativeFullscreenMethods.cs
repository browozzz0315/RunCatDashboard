using System.Runtime.InteropServices;

namespace RunCatDashboard.App.Interop;

internal static class NativeFullscreenMethods
{
    internal const uint MonitorDefaultToNull = 0x00000000;
    internal const uint EventSystemForeground = 0x0003;
    internal const uint WinEventOutOfContext = 0x0000;
    internal const uint WinEventSkipOwnProcess = 0x0002;
    internal const uint DwmWindowAttributeExtendedFrameBounds = 9;

    internal delegate void WinEventDelegate(
        nint eventHook,
        uint eventType,
        nint windowHandle,
        int objectId,
        int childId,
        uint eventThread,
        uint eventTime);

    [DllImport("user32.dll")]
    internal static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowVisible(nint windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsIconic(nint windowHandle);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowRect(
        nint windowHandle,
        out NativeRectangle rectangle);

    [DllImport("dwmapi.dll")]
    internal static extern int DwmGetWindowAttribute(
        nint windowHandle,
        uint attribute,
        out NativeRectangle value,
        uint valueSize);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern nint SetWinEventHook(
        uint eventMinimum,
        uint eventMaximum,
        nint eventHookModule,
        WinEventDelegate callback,
        uint processId,
        uint threadId,
        uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnhookWinEvent(nint eventHook);
}
