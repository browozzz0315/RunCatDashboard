using RunCatDashboard.App.Models;
using RunCatDashboard.App.Services;

namespace RunCatDashboard.Tests.Services;

public sealed class WindowsSystemMetricsServiceTests
{
    private static readonly DateTimeOffset SampleTime =
        new(2026, 7, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SampleAsync_FirstSample_HasNoCpuPercentage()
    {
        var source = new FakeWindowsSystemMetricsSource(
            [new CpuTimes(100, 300, 200)],
            new PhysicalMemoryStatus(1_000, 250));
        var service = CreateService(source);

        SystemMetricsSnapshot result = await service.SampleAsync();

        Assert.Null(result.CpuUsagePercent);
        Assert.Equal(1, source.CpuReadCount);
    }

    [Fact]
    public async Task SampleAsync_SubsequentSample_UsesPreviousCumulativeCpuTimes()
    {
        var source = new FakeWindowsSystemMetricsSource(
            [
                new CpuTimes(100, 300, 200),
                new CpuTimes(150, 400, 300)
            ],
            new PhysicalMemoryStatus(1_000, 250));
        var service = CreateService(source);

        await service.SampleAsync();
        SystemMetricsSnapshot result = await service.SampleAsync();

        Assert.Equal(75d, Assert.IsType<double>(result.CpuUsagePercent));
    }

    [Fact]
    public async Task SampleAsync_UsesBytesConsistentlyInSnapshot()
    {
        var source = new FakeWindowsSystemMetricsSource(
            [new CpuTimes(100, 300, 200)],
            new PhysicalMemoryStatus(8UL * 1024 * 1024 * 1024, 2UL * 1024 * 1024 * 1024));
        var service = CreateService(source);

        SystemMetricsSnapshot result = await service.SampleAsync();

        Assert.Equal(SampleTime, result.SampledAt);
        Assert.Equal(75d, result.MemoryUsagePercent);
        Assert.Equal(6UL * 1024 * 1024 * 1024, result.UsedPhysicalMemoryBytes);
        Assert.Equal(8UL * 1024 * 1024 * 1024, result.TotalPhysicalMemoryBytes);
        Assert.Equal(
            2UL * 1024 * 1024 * 1024,
            result.TotalPhysicalMemoryBytes - result.UsedPhysicalMemoryBytes);
    }

    [Fact]
    public async Task SampleAsync_WithCancellation_DoesNotReadNativeSource()
    {
        var source = new FakeWindowsSystemMetricsSource(
            [new CpuTimes(100, 300, 200)],
            new PhysicalMemoryStatus(1_000, 250));
        var service = CreateService(source);
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await service.SampleAsync(cancellationSource.Token));
        Assert.Equal(0, source.CpuReadCount);
    }

    private static WindowsSystemMetricsService CreateService(
        IWindowsSystemMetricsSource source)
    {
        return new WindowsSystemMetricsService(source, new FixedTimeProvider(SampleTime));
    }

    private sealed class FakeWindowsSystemMetricsSource : IWindowsSystemMetricsSource
    {
        private readonly Queue<CpuTimes> _cpuTimes;
        private readonly PhysicalMemoryStatus _memoryStatus;

        internal FakeWindowsSystemMetricsSource(
            IEnumerable<CpuTimes> cpuTimes,
            PhysicalMemoryStatus memoryStatus)
        {
            _cpuTimes = new Queue<CpuTimes>(cpuTimes);
            _memoryStatus = memoryStatus;
        }

        internal int CpuReadCount { get; private set; }

        public CpuTimes GetCpuTimes()
        {
            CpuReadCount++;
            return _cpuTimes.Dequeue();
        }

        public PhysicalMemoryStatus GetPhysicalMemoryStatus()
        {
            return _memoryStatus;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset timestamp) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return timestamp;
        }
    }
}
