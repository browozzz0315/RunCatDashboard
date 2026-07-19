using RunCatDashboard.App.Animation;

namespace RunCatDashboard.Tests.Animation;

public sealed class CpuAnimationSpeedMapperTests
{
    [Theory]
    [InlineData(0d, 250d)]
    [InlineData(25d, 200d)]
    [InlineData(50d, 150d)]
    [InlineData(75d, 100d)]
    [InlineData(100d, 50d)]
    [InlineData(-1d, 250d)]
    [InlineData(101d, 50d)]
    public void Map_FiniteCpu_UsesClampedLinearMapping(
        double cpuPercentage,
        double expectedMilliseconds)
    {
        TimeSpan result = CpuAnimationSpeedMapper.Map(cpuPercentage);

        Assert.Equal(expectedMilliseconds, result.TotalMilliseconds);
    }

    [Fact]
    public void Map_NullOrNonFiniteCpu_UsesSafeDefault()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(250), CpuAnimationSpeedMapper.Map(null));
        Assert.Equal(TimeSpan.FromMilliseconds(250), CpuAnimationSpeedMapper.Map(double.NaN));
        Assert.Equal(TimeSpan.FromMilliseconds(250), CpuAnimationSpeedMapper.Map(double.PositiveInfinity));
        Assert.Equal(TimeSpan.FromMilliseconds(250), CpuAnimationSpeedMapper.Map(double.NegativeInfinity));
    }

    [Fact]
    public void Map_AsCpuIncreases_IntervalNeverIncreasesAndAlwaysStaysBounded()
    {
        TimeSpan previous = CpuAnimationSpeedMapper.Map(-20d);
        for (int cpu = -19; cpu <= 120; cpu++)
        {
            TimeSpan current = CpuAnimationSpeedMapper.Map(cpu);
            Assert.True(current <= previous);
            Assert.InRange(current.TotalMilliseconds, 50d, 250d);
            previous = current;
        }
    }
}
