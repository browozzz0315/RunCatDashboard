using System.Runtime.InteropServices;

namespace RunCatDashboard.App.Interop;

internal static class NativeShellMethods
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern uint RegisterWindowMessage(string messageName);
}
