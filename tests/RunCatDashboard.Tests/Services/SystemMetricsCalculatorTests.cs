using RunCatDashboard.App.Services;

namespace RunCatDashboard.Tests.Services;

public sealed class SystemMetricsCalculatorTests
{
    [Fact]
    public void CalculateCpuUsagePercent_WithoutPreviousSample_ReturnsNull()
    {
        var current = new CpuTimes(100, 300, 200);

        double? result = SystemMetricsCalculator.CalculateCpuUsagePercent(null, current);

        Assert.Null(result);
    }

    [Fact]
    public void CalculateCpuUsagePercent_WithValidDeltas_ReturnsBusyPercentage()
    {
        var previous = new CpuTimes(100, 300, 200);
        var current = new CpuTimes(150, 400, 300);

        double? result = SystemMetricsCalculator.CalculateCpuUsagePercent(previous, current);

        Assert.Equal(75d, Assert.IsType<double>(result));
    }

    [Theory]
    [InlineData(100UL, 100UL, 0d)]
    [InlineData(1UL, 1000UL, 99.9d)]
    public void CalculateCpuUsagePercent_AtValidBoundaries_ReturnsExpectedValue(
        ulong idleDelta,
        ulong totalDelta,
        double expected)
    {
        var previous = new CpuTimes(10, 20, 30);
        var current = new CpuTimes(
            10 + idleDelta,
            20 + totalDelta,
            30);

        double? result = SystemMetricsCalculator.CalculateCpuUsagePercent(previous, current);

        Assert.Equal(expected, Assert.IsType<double>(result), 10);
    }

    [Fact]
    public void CalculateCpuUsagePercent_WithInvalidDelta_ReturnsNull()
    {
        (CpuTimes Previous, CpuTimes Current)[] invalidSamples =
        [
            (new CpuTimes(10, 20, 30), new CpuTimes(10, 20, 30)),
            (new CpuTimes(10, 20, 30), new CpuTimes(9, 30, 40)),
            (new CpuTimes(10, 20, 30), new CpuTimes(30, 25, 35)),
            (new CpuTimes(0, 0, 0), new CpuTimes(0, ulong.MaxValue, 1))
        ];

        foreach ((CpuTimes previous, CpuTimes current) in invalidSamples)
        {
            double? result = SystemMetricsCalculator.CalculateCpuUsagePercent(previous, current);

            Assert.Null(result);
        }
    }

    [Theory]
    [InlineData(-1d, 0d)]
    [InlineData(0d, 0d)]
    [InlineData(50d, 50d)]
    [InlineData(100d, 100d)]
    [InlineData(101d, 100d)]
    public void ClampPercentage_LimitsFiniteValueToValidRange(double value, double expected)
    {
        Assert.Equal(expected, SystemMetricsCalculator.ClampPercentage(value));
    }

    [Fact]
    public void ClampPercentage_WithNonFiniteValue_ReturnsZero()
    {
        Assert.Equal(0d, SystemMetricsCalculator.ClampPercentage(double.NaN));
        Assert.Equal(0d, SystemMetricsCalculator.ClampPercentage(double.PositiveInfinity));
        Assert.Equal(0d, SystemMetricsCalculator.ClampPercentage(double.NegativeInfinity));
    }

    [Fact]
    public void CalculateMemoryUsage_WithValidValues_ReturnsConsistentBytesAndPercentage()
    {
        var status = new PhysicalMemoryStatus(1_000, 250);

        MemoryUsage result = SystemMetricsCalculator.CalculateMemoryUsage(status);

        Assert.Equal(75d, result.UsagePercent);
        Assert.Equal(750UL, result.UsedBytes);
        Assert.Equal(1_000UL, result.TotalBytes);
    }

    [Fact]
    public void CalculateMemoryUsage_WithZeroTotal_ReturnsZeroValues()
    {
        var status = new PhysicalMemoryStatus(0, 100);

        MemoryUsage result = SystemMetricsCalculator.CalculateMemoryUsage(status);

        Assert.Equal(new MemoryUsage(0d, 0, 0), result);
    }

    [Fact]
    public void CalculateMemoryUsage_WithAvailableGreaterThanTotal_ClampsUsedValues()
    {
        var status = new PhysicalMemoryStatus(1_000, 1_500);

        MemoryUsage result = SystemMetricsCalculator.CalculateMemoryUsage(status);

        Assert.Equal(new MemoryUsage(0d, 0, 1_000), result);
    }
}
