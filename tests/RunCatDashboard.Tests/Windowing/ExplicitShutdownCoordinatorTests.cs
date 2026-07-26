using System.IO;
using Microsoft.Extensions.Logging;
using RunCatDashboard.App.Diagnostics;
using RunCatDashboard.App.Settings;
using RunCatDashboard.App.Windowing;

namespace RunCatDashboard.Tests.Windowing;

public sealed class ExplicitShutdownCoordinatorTests
{
    [Fact]
    public async Task Exit_IsIdempotentAndFlushesBeforeClosingBothWindowsAndShutdown()
    {
        var order = new List<string>();
        var settings = new FakeSettingsService(order);
        var visibility = new WindowVisibilityCoordinator();
        var coordinator = new ExplicitShutdownCoordinator(
            visibility,
            settings,
            new FakeLoggingRuntime(order));

        bool first = await coordinator.ShutdownAsync(
            () => order.Add("capture"),
            () => order.Add("settings-close"),
            () => order.Add("main-close"),
            () => order.Add("shutdown"));
        bool second = await coordinator.ShutdownAsync(
            () => order.Add("capture-again"),
            () => order.Add("settings-close-again"),
            () => order.Add("main-close-again"),
            () => order.Add("shutdown-again"));

        Assert.True(first);
        Assert.False(second);
        Assert.Equal(
            ["capture", "flush", "settings-close", "main-close", "log-flush", "shutdown"],
            order);
    }

    [Fact]
    public async Task LoggingFlushFailure_DoesNotPreventApplicationShutdown()
    {
        var order = new List<string>();
        var coordinator = new ExplicitShutdownCoordinator(
            new WindowVisibilityCoordinator(),
            new FakeSettingsService(order),
            new ThrowingLoggingRuntime());

        bool exited = await coordinator.ShutdownAsync(
            () => order.Add("capture"),
            () => order.Add("settings-close"),
            () => order.Add("main-close"),
            () => order.Add("shutdown"));

        Assert.True(exited);
        Assert.Equal(
            ["capture", "flush", "settings-close", "main-close", "shutdown"],
            order);
    }

    private sealed class FakeSettingsService(List<string> order) : ISettingsService
    {
        public AppSettings Current => AppSettings.Defaults;
        public string? LastDiagnostic => null;
        public event Action<AppSettings>? Changed { add { } remove { } }
        public event Action<string?>? DiagnosticChanged { add { } remove { } }
        public Task LoadAsync(CancellationToken token = default) => Task.CompletedTask;
        public bool Update(Func<AppSettings, AppSettings> update) => false;
        public Task FlushAsync(CancellationToken token = default)
        {
            order.Add("flush");
            return Task.CompletedTask;
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeLoggingRuntime(List<string> order) : IApplicationLoggingRuntime
    {
        public ILoggerFactory LoggerFactory { get; } =
            Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance;

        public Task FlushAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            Assert.Equal(TimeSpan.FromSeconds(2), timeout);
            order.Add("log-flush");
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ThrowingLoggingRuntime : IApplicationLoggingRuntime
    {
        public ILoggerFactory LoggerFactory { get; } =
            Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance;

        public Task FlushAsync(TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new IOException("configured logging flush failure");

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
