using System.Runtime.InteropServices;

namespace RunCatDashboard.App.Interop;

internal static class NativeWindowMethods
{
    internal const int ExtendedWindowStyleIndex = -20;

    [Flags]
    internal enum SetWindowPositionFlags : uint
    {
        NoSize = 0x0001,
        NoMove = 0x0002,
        NoZOrder = 0x0004,
        NoActivate = 0x0010,
        FrameChanged = 0x0020
    }

    internal static nint GetWindowLongPointer(nint windowHandle, int index)
    {
        return nint.Size == sizeof(long)
            ? GetWindowLongPointer64(windowHandle, index)
            : new nint(GetWindowLong32(windowHandle, index));
    }

    internal static nint SetWindowLongPointer(nint windowHandle, int index, nint value)
    {
        return nint.Size == sizeof(long)
            ? SetWindowLongPointer64(windowHandle, index, value)
            : new nint(SetWindowLong32(windowHandle, index, value.ToInt32()));
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern nint GetWindowLongPointer64(nint windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong32(nint windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPointer64(
        nint windowHandle,
        int index,
        nint newValue);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong32(
        nint windowHandle,
        int index,
        int newValue);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowPos(
        nint windowHandle,
        nint insertAfterWindowHandle,
        int x,
        int y,
        int width,
        int height,
        SetWindowPositionFlags flags);
}
