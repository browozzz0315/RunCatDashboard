using RunCatDashboard.App.Models;

namespace RunCatDashboard.App.Services;

public sealed class WindowsSystemMetricsService : ISystemMetricsService
{
    private readonly Lock _lock = new();
    private readonly IWindowsSystemMetricsSource _source;
    private readonly TimeProvider _timeProvider;
    private CpuTimes? _previousCpuTimes;

    public WindowsSystemMetricsService()
        : this(new WindowsSystemMetricsSource(), TimeProvider.System)
    {
    }

    internal WindowsSystemMetricsService(
        IWindowsSystemMetricsSource source,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _source = source;
        _timeProvider = timeProvider;
    }

    public ValueTask<SystemMetricsSnapshot> SampleAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_lock)
        {
            cancellationToken.ThrowIfCancellationRequested();

            CpuTimes cpuTimes = _source.GetCpuTimes();
            cancellationToken.ThrowIfCancellationRequested();

            PhysicalMemoryStatus memoryStatus = _source.GetPhysicalMemoryStatus();
            cancellationToken.ThrowIfCancellationRequested();

            double? cpuUsagePercent = SystemMetricsCalculator.CalculateCpuUsagePercent(
                _previousCpuTimes,
                cpuTimes);
            MemoryUsage memoryUsage = SystemMetricsCalculator.CalculateMemoryUsage(memoryStatus);
            _previousCpuTimes = cpuTimes;

            var snapshot = new SystemMetricsSnapshot(
                _timeProvider.GetUtcNow(),
                cpuUsagePercent,
                memoryUsage.UsagePercent,
                memoryUsage.UsedBytes,
                memoryUsage.TotalBytes);

            return ValueTask.FromResult(snapshot);
        }
    }
}
