using System.ComponentModel;
using System.Runtime.InteropServices;

namespace RunCatDashboard.App.Interop;

internal interface IRegisteredWindowMessageApi
{
    int Register(string messageName);
}

internal sealed class Win32RegisteredWindowMessageApi : IRegisteredWindowMessageApi
{
    public int Register(string messageName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageName);
        uint message = NativeShellMethods.RegisterWindowMessage(messageName);
        if (message == 0)
        {
            throw new Win32Exception(
                Marshal.GetLastPInvokeError(),
                $"RegisterWindowMessage failed for '{messageName}'.");
        }

        return checked((int)message);
    }
}
