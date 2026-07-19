namespace RunCatDashboard.App.Animation;

internal static class RecentCpuSampleAverager
{
    internal const int MaximumSampleCount = 3;

    internal static double? Average(IEnumerable<double?> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);

        var recentSamples = new Queue<double>(MaximumSampleCount);
        foreach (double? sample in samples)
        {
            if (!sample.HasValue || !double.IsFinite(sample.Value))
            {
                continue;
            }

            if (recentSamples.Count == MaximumSampleCount)
            {
                recentSamples.Dequeue();
            }

            recentSamples.Enqueue(Math.Clamp(sample.Value, 0d, 100d));
        }

        return recentSamples.Count == 0 ? null : recentSamples.Average();
    }
}
