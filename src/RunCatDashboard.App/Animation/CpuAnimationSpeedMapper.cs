namespace RunCatDashboard.App.Animation;

internal static class CpuAnimationSpeedMapper
{
    internal static readonly TimeSpan FastestInterval = TimeSpan.FromMilliseconds(50);
    internal static readonly TimeSpan SlowestInterval = TimeSpan.FromMilliseconds(250);

    internal static TimeSpan Map(double? cpuPercentage)
    {
        if (!cpuPercentage.HasValue || !double.IsFinite(cpuPercentage.Value))
        {
            return SlowestInterval;
        }

        double clampedCpu = Math.Clamp(cpuPercentage.Value, 0d, 100d);
        double intervalMilliseconds =
            SlowestInterval.TotalMilliseconds - (200d * clampedCpu / 100d);
        return TimeSpan.FromMilliseconds(
            Math.Clamp(
                intervalMilliseconds,
                FastestInterval.TotalMilliseconds,
                SlowestInterval.TotalMilliseconds));
    }
}
