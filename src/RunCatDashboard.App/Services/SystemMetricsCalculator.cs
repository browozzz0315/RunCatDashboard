namespace RunCatDashboard.App.Services;

internal readonly record struct CpuTimes(
    ulong IdleTime,
    ulong KernelTime,
    ulong UserTime);

internal readonly record struct PhysicalMemoryStatus(
    ulong TotalBytes,
    ulong AvailableBytes);

internal readonly record struct MemoryUsage(
    double UsagePercent,
    ulong UsedBytes,
    ulong TotalBytes);

internal static class SystemMetricsCalculator
{
    internal static double? CalculateCpuUsagePercent(CpuTimes? previous, CpuTimes current)
    {
        if (previous is not CpuTimes baseline)
        {
            return null;
        }

        if (current.IdleTime < baseline.IdleTime ||
            current.KernelTime < baseline.KernelTime ||
            current.UserTime < baseline.UserTime)
        {
            return null;
        }

        ulong idleDelta = current.IdleTime - baseline.IdleTime;
        ulong kernelDelta = current.KernelTime - baseline.KernelTime;
        ulong userDelta = current.UserTime - baseline.UserTime;

        if (ulong.MaxValue - kernelDelta < userDelta)
        {
            return null;
        }

        ulong totalDelta = kernelDelta + userDelta;
        if (totalDelta == 0 || idleDelta > totalDelta)
        {
            return null;
        }

        ulong busyDelta = totalDelta - idleDelta;
        double usagePercent = (double)busyDelta / totalDelta * 100d;
        return ClampPercentage(usagePercent);
    }

    internal static MemoryUsage CalculateMemoryUsage(PhysicalMemoryStatus status)
    {
        if (status.TotalBytes == 0)
        {
            return new MemoryUsage(0d, 0, 0);
        }

        ulong availableBytes = Math.Min(status.AvailableBytes, status.TotalBytes);
        ulong usedBytes = status.TotalBytes - availableBytes;
        double usagePercent = (double)usedBytes / status.TotalBytes * 100d;

        return new MemoryUsage(
            ClampPercentage(usagePercent),
            usedBytes,
            status.TotalBytes);
    }

    internal static double ClampPercentage(double value)
    {
        if (!double.IsFinite(value))
        {
            return 0d;
        }

        return Math.Clamp(value, 0d, 100d);
    }
}
