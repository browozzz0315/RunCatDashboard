namespace RunCatDashboard.App.Models;

public sealed record SystemMetricsSnapshot(
    DateTimeOffset SampledAt,
    double? CpuUsagePercent,
    double MemoryUsagePercent,
    ulong UsedPhysicalMemoryBytes,
    ulong TotalPhysicalMemoryBytes);
