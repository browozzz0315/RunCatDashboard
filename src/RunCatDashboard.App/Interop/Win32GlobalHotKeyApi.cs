using System.ComponentModel;
using System.Runtime.InteropServices;

namespace RunCatDashboard.App.Interop;

internal interface INativeGlobalHotKeyApi
{
    void Register(nint windowHandle, int identifier, uint modifiers, uint virtualKey);

    void Unregister(nint windowHandle, int identifier);
}

internal sealed class Win32GlobalHotKeyApi : INativeGlobalHotKeyApi
{
    public void Register(nint windowHandle, int identifier, uint modifiers, uint virtualKey)
    {
        if (!NativeGlobalHotKeyMethods.RegisterHotKey(
                windowHandle,
                identifier,
                modifiers,
                virtualKey))
        {
            throw new Win32Exception(
                Marshal.GetLastPInvokeError(),
                "RegisterHotKey failed.");
        }
    }

    public void Unregister(nint windowHandle, int identifier)
    {
        if (!NativeGlobalHotKeyMethods.UnregisterHotKey(windowHandle, identifier))
        {
            throw new Win32Exception(
                Marshal.GetLastPInvokeError(),
                "UnregisterHotKey failed.");
        }
    }
}
