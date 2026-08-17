using RunCatDashboard.App.Animation;
using RunCatDashboard.App.Settings;

namespace RunCatDashboard.Tests.Animation;

public sealed class AnimationTimingCalculatorTests
{
    [Theory]
    [InlineData(AnimationSpeedPreference.Slow, 0.75)]
    [InlineData(AnimationSpeedPreference.Normal, 1.00)]
    [InlineData(AnimationSpeedPreference.Fast, 1.25)]
    public void SpeedPreference_UsesSpecifiedMultiplier(
        AnimationSpeedPreference preference,
        double expectedMultiplier)
    {
        Assert.Equal(expectedMultiplier,
            AnimationTimingCalculator.GetSpeedMultiplier(preference));
    }

    [Fact]
    public void BuiltInBaseIntervalWithNormal_PreservesExistingCpuMapping()
    {
        TimeSpan expected = CpuAnimationSpeedMapper.Map(50);

        Assert.Equal(
            expected,
            AnimationTimingCalculator.Calculate(50, 250, AnimationSpeedPreference.Normal));
    }

    [Fact]
    public void FasterPreferenceProducesSmallerIntervalUnlessClamped()
    {
        TimeSpan slow = AnimationTimingCalculator.Calculate(
            50, 250, AnimationSpeedPreference.Slow);
        TimeSpan normal = AnimationTimingCalculator.Calculate(
            50, 250, AnimationSpeedPreference.Normal);
        TimeSpan fast = AnimationTimingCalculator.Calculate(
            50, 250, AnimationSpeedPreference.Fast);

        Assert.True(slow > normal);
        Assert.True(normal > fast);
        Assert.Equal(TimeSpan.FromMilliseconds(50),
            AnimationTimingCalculator.Calculate(100, 250, AnimationSpeedPreference.Fast));
        Assert.Equal(TimeSpan.FromMilliseconds(250),
            AnimationTimingCalculator.Calculate(0, 250, AnimationSpeedPreference.Slow));
    }

    [Fact]
    public void BaseIntervalParticipatesInFormulaAndRetainsBounds()
    {
        Assert.Equal(
            TimeSpan.FromMilliseconds(120),
            AnimationTimingCalculator.Calculate(50, 200, AnimationSpeedPreference.Normal));
        Assert.InRange(
            AnimationTimingCalculator.Calculate(0, 1000, AnimationSpeedPreference.Normal).TotalMilliseconds,
            249.999,
            250.001);
    }
}
