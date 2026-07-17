using System.ComponentModel;
using System.Runtime.InteropServices;
using RunCatDashboard.App.Interop;

namespace RunCatDashboard.App.Services;

internal interface IWindowsSystemMetricsSource
{
    CpuTimes GetCpuTimes();

    PhysicalMemoryStatus GetPhysicalMemoryStatus();
}

internal sealed class WindowsSystemMetricsSource : IWindowsSystemMetricsSource
{
    public CpuTimes GetCpuTimes()
    {
        if (!NativeMethods.GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        return new CpuTimes(idle.ToUInt64(), kernel.ToUInt64(), user.ToUInt64());
    }

    public PhysicalMemoryStatus GetPhysicalMemoryStatus()
    {
        var status = new MemoryStatusEx
        {
            Length = (uint)Marshal.SizeOf<MemoryStatusEx>()
        };

        if (!NativeMethods.GlobalMemoryStatusEx(ref status))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        return new PhysicalMemoryStatus(status.TotalPhysical, status.AvailablePhysical);
    }
}
