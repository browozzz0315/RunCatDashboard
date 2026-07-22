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
        var coordinator = new ExplicitShutdownCoordinator(visibility, settings);

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
}
