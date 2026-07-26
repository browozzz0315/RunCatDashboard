using Microsoft.Extensions.Logging;

namespace RunCatDashboard.App.Diagnostics;

internal sealed record LoggingPolicy(
    bool IsDevelopment,
    LogLevel? CommandLineOverride,
    bool IsHighFrequencyTraceEnabled)
{
    internal const string LifecycleCategory = "RunCatDashboard.Lifecycle";
    internal const string HighFrequencyCategoryPrefix = "RunCatDashboard.HighFrequency";
    internal const string TraceArgument = "--log-level";
    internal const string TraceValue = "Trace";
    internal const string HighFrequencyTraceArgument = "--enable-high-frequency-trace";

    internal static LoggingPolicy Create(string buildConfiguration, IReadOnlyList<string> arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(buildConfiguration);
        ArgumentNullException.ThrowIfNull(arguments);

        bool traceRequested = false;
        bool highFrequencyRequested = false;
        for (int index = 0; index < arguments.Count; index++)
        {
            string argument = arguments[index];
            if (string.Equals(argument, HighFrequencyTraceArgument, StringComparison.OrdinalIgnoreCase))
            {
                highFrequencyRequested = true;
                continue;
            }

            if (string.Equals(argument, $"{TraceArgument}={TraceValue}", StringComparison.OrdinalIgnoreCase))
            {
                traceRequested = true;
                continue;
            }

            if (string.Equals(argument, TraceArgument, StringComparison.OrdinalIgnoreCase) &&
                index + 1 < arguments.Count &&
                string.Equals(arguments[index + 1], TraceValue, StringComparison.OrdinalIgnoreCase))
            {
                traceRequested = true;
                index++;
            }
        }

        return new LoggingPolicy(
            string.Equals(buildConfiguration, "Debug", StringComparison.OrdinalIgnoreCase),
            traceRequested ? LogLevel.Trace : null,
            highFrequencyRequested);
    }

    internal bool IsEnabled(string? category, LogLevel level)
    {
        if (level == LogLevel.None)
        {
            return false;
        }

        category ??= string.Empty;
        if (category.StartsWith(HighFrequencyCategoryPrefix, StringComparison.Ordinal))
        {
            return IsHighFrequencyTraceEnabled &&
                   CommandLineOverride == LogLevel.Trace &&
                   level >= LogLevel.Trace;
        }

        if (category.StartsWith("Microsoft", StringComparison.Ordinal) ||
            category.StartsWith("System", StringComparison.Ordinal))
        {
            return level >= LogLevel.Warning;
        }

        if (CommandLineOverride == LogLevel.Trace &&
            category.StartsWith("RunCatDashboard", StringComparison.Ordinal))
        {
            return level >= LogLevel.Trace;
        }

        if (IsDevelopment && category.StartsWith("RunCatDashboard", StringComparison.Ordinal))
        {
            return level >= LogLevel.Debug;
        }

        if (string.Equals(category, LifecycleCategory, StringComparison.Ordinal))
        {
            return level >= LogLevel.Information;
        }

        return level >= LogLevel.Warning;
    }
}
