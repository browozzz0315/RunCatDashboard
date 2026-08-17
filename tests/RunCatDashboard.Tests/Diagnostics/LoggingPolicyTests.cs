using Microsoft.Extensions.Logging;
using RunCatDashboard.App.Diagnostics;
using RunCatDashboard.App.Settings;

namespace RunCatDashboard.Tests.Diagnostics;

public sealed class LoggingPolicyTests
{
    [Fact]
    public void Development_EnablesApplicationDebugButNotTrace()
    {
        LoggingPolicy policy = LoggingPolicy.Create("Debug", []);

        Assert.True(policy.IsEnabled("RunCatDashboard.App.Services", LogLevel.Debug));
        Assert.False(policy.IsEnabled("RunCatDashboard.App.Services", LogLevel.Trace));
        Assert.False(policy.IsEnabled("Microsoft.Hosting", LogLevel.Information));
        Assert.True(policy.IsEnabled("Microsoft.Hosting", LogLevel.Warning));
    }

    [Fact]
    public void Release_EnablesLifecycleInformationAndOtherApplicationWarningsOnly()
    {
        LoggingPolicy policy = LoggingPolicy.Create("Release", []);

        Assert.True(policy.IsEnabled(LoggingPolicy.LifecycleCategory, LogLevel.Information));
        Assert.False(policy.IsEnabled("RunCatDashboard.App.Services", LogLevel.Information));
        Assert.True(policy.IsEnabled("RunCatDashboard.App.Services", LogLevel.Warning));
        Assert.False(policy.IsEnabled("RunCatDashboard.App.Services", LogLevel.Debug));
        Assert.False(policy.IsEnabled("RunCatDashboard.App.Services", LogLevel.Trace));
    }

    [Fact]
    public void TraceOverride_DoesNotEnableHighFrequencyCategoryWithoutSeparateGate()
    {
        LoggingPolicy traceOnly = LoggingPolicy.Create(
            "Release",
            ["--log-level", "Trace"]);
        LoggingPolicy traceAndHighFrequency = LoggingPolicy.Create(
            "Release",
            ["--log-level=Trace", "--enable-high-frequency-trace"]);

        Assert.True(traceOnly.IsEnabled("RunCatDashboard.App.Services", LogLevel.Trace));
        Assert.False(traceOnly.IsEnabled("RunCatDashboard.HighFrequency.Fullscreen", LogLevel.Trace));
        Assert.True(traceAndHighFrequency.IsEnabled(
            "RunCatDashboard.HighFrequency.Fullscreen",
            LogLevel.Trace));
    }

    [Fact]
    public void CommandLineOverrides_AreNotRetainedByLaterPolicyInstances()
    {
        _ = LoggingPolicy.Create("Release", ["--log-level", "Trace"]);

        LoggingPolicy later = LoggingPolicy.Create("Release", []);

        Assert.Null(later.CommandLineOverride);
        Assert.False(later.IsHighFrequencyTraceEnabled);
    }

    [Fact]
    public void DiagnosticLogging_DoesNotChangeSettingsSchemaVersion()
    {
        Assert.Equal(6, AppSettings.CurrentVersion);
    }
}
