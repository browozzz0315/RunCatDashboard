using RunCatDashboard.App.Settings;

namespace RunCatDashboard.App.Animation;

internal static class AnimationTimingCalculator
{
    internal const double DefaultBaseFrameIntervalMilliseconds = 250d;

    internal static double GetSpeedMultiplier(AnimationSpeedPreference preference) =>
        preference switch
        {
            AnimationSpeedPreference.Slow => 0.75d,
            AnimationSpeedPreference.Normal => 1.00d,
            AnimationSpeedPreference.Fast => 1.25d,
            _ => 1.00d
        };

    internal static TimeSpan Calculate(
        double? cpuPercentage,
        double baseFrameIntervalMilliseconds,
        AnimationSpeedPreference speedPreference)
    {
        if (!double.IsFinite(baseFrameIntervalMilliseconds) || baseFrameIntervalMilliseconds <= 0)
        {
            baseFrameIntervalMilliseconds = DefaultBaseFrameIntervalMilliseconds;
        }

        TimeSpan cpuMapped = CpuAnimationSpeedMapper.Map(cpuPercentage);
        double multiplier = GetSpeedMultiplier(speedPreference);
        if (baseFrameIntervalMilliseconds == DefaultBaseFrameIntervalMilliseconds &&
            multiplier == 1d)
        {
            return cpuMapped;
        }
        double effectiveMilliseconds = baseFrameIntervalMilliseconds *
            (cpuMapped.TotalMilliseconds / DefaultBaseFrameIntervalMilliseconds) /
            multiplier;
        return TimeSpan.FromMilliseconds(Math.Clamp(
            effectiveMilliseconds,
            CpuAnimationSpeedMapper.FastestInterval.TotalMilliseconds,
            CpuAnimationSpeedMapper.SlowestInterval.TotalMilliseconds));
    }
}
