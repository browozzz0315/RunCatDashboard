using System.ComponentModel;
using System.Runtime.InteropServices;

namespace RunCatDashboard.App.Interop;

internal interface INativeWindowStyleApi
{
    long GetExtendedStyle(nint windowHandle);

    void SetExtendedStyle(nint windowHandle, long style);

    void RefreshFrame(nint windowHandle);
}

internal sealed class Win32NativeWindowStyleApi : INativeWindowStyleApi
{
    public long GetExtendedStyle(nint windowHandle)
    {
        Marshal.SetLastPInvokeError(0);
        nint result = NativeWindowMethods.GetWindowLongPointer(
            windowHandle,
            NativeWindowMethods.ExtendedWindowStyleIndex);
        int errorCode = Marshal.GetLastPInvokeError();

        if (result == nint.Zero && errorCode != 0)
        {
            throw new Win32Exception(errorCode, "GetWindowLongPtr failed for GWL_EXSTYLE.");
        }

        return NativeWindowStyleBits.FromNativeValue(result);
    }

    public void SetExtendedStyle(nint windowHandle, long style)
    {
        Marshal.SetLastPInvokeError(0);
        nint result = NativeWindowMethods.SetWindowLongPointer(
            windowHandle,
            NativeWindowMethods.ExtendedWindowStyleIndex,
            NativeWindowStyleBits.ToNativeValue(style));
        int errorCode = Marshal.GetLastPInvokeError();

        if (result == nint.Zero && errorCode != 0)
        {
            throw new Win32Exception(errorCode, "SetWindowLongPtr failed for GWL_EXSTYLE.");
        }
    }

    public void RefreshFrame(nint windowHandle)
    {
        const NativeWindowMethods.SetWindowPositionFlags flags =
            NativeWindowMethods.SetWindowPositionFlags.NoMove |
            NativeWindowMethods.SetWindowPositionFlags.NoSize |
            NativeWindowMethods.SetWindowPositionFlags.NoZOrder |
            NativeWindowMethods.SetWindowPositionFlags.NoActivate |
            NativeWindowMethods.SetWindowPositionFlags.FrameChanged;

        if (!NativeWindowMethods.SetWindowPos(
                windowHandle,
                nint.Zero,
                0,
                0,
                0,
                0,
                flags))
        {
            throw new Win32Exception(
                Marshal.GetLastPInvokeError(),
                "SetWindowPos failed while refreshing the window frame.");
        }
    }
}
