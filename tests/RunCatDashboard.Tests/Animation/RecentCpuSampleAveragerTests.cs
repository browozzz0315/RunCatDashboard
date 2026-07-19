using RunCatDashboard.App.Animation;

namespace RunCatDashboard.Tests.Animation;

public sealed class RecentCpuSampleAveragerTests
{
    [Theory]
    [MemberData(nameof(AverageCases))]
    public void Average_UsesAtMostThreeNewestValidSamples(
        double?[] samples,
        double? expected)
    {
        double? result = RecentCpuSampleAverager.Average(samples);

        Assert.Equal(expected, result);
    }

    public static TheoryData<double?[], double?> AverageCases => new()
    {
        { [30d], 30d },
        { [20d, 40d], 30d },
        { [10d, 20d, 30d], 20d },
        { [5d, 10d, 20d, 30d], 20d },
        { [double.NaN, double.PositiveInfinity, double.NegativeInfinity], null },
        { [10d, double.NaN, 20d, double.PositiveInfinity, 30d], 20d },
        { [null, double.NaN, 40d, null, 80d], 60d },
        { [], null },
        { [-20d, 50d, 120d], 50d },
        { [10d, 20d, 30d, double.NaN, 90d], (20d + 30d + 90d) / 3d }
    };
}
