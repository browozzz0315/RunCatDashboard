using System.Runtime.InteropServices;

namespace RunCatDashboard.App.Interop;

internal static class NativeMethods
{
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetSystemTimes(
        out FileTime idleTime,
        out FileTime kernelTime,
        out FileTime userTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
}

[StructLayout(LayoutKind.Sequential)]
internal struct FileTime
{
    internal uint LowDateTime;
    internal uint HighDateTime;

    internal readonly ulong ToUInt64()
    {
        return ((ulong)HighDateTime << 32) | LowDateTime;
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct MemoryStatusEx
{
    internal uint Length;
    internal uint MemoryLoad;
    internal ulong TotalPhysical;
    internal ulong AvailablePhysical;
    internal ulong TotalPageFile;
    internal ulong AvailablePageFile;
    internal ulong TotalVirtual;
    internal ulong AvailableVirtual;
    internal ulong AvailableExtendedVirtual;
}
