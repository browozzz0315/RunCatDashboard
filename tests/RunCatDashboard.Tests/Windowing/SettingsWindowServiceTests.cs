using RunCatDashboard.App.Windowing;

namespace RunCatDashboard.Tests.Windowing;

public sealed class SettingsWindowServiceTests
{
    [Fact]
    public void RepeatedOpen_ActivatesSingleInstanceAndRestoresMinimizedWindow()
    {
        int createCount = 0;
        var host = new FakeHost { IsMinimized = true };
        var service = new SettingsWindowService(() =>
        {
            createCount++;
            return host;
        });

        service.Open();
        service.Open();

        Assert.Equal(1, createCount);
        Assert.Equal(1, host.ShowCount);
        Assert.Equal(2, host.ActivateCount);
        Assert.Equal(1, host.RestoreCount);
    }

    [Fact]
    public void ClosedWindow_CanBeCreatedAgain()
    {
        var hosts = new Queue<FakeHost>([new FakeHost(), new FakeHost()]);
        var service = new SettingsWindowService(() => hosts.Dequeue());
        service.Open();
        service.Close();

        service.Open();

        Assert.True(service.IsOpen);
        Assert.Empty(hosts);
    }

    [Fact]
    public void DashboardHidden_DoesNotPreventOpeningSettings()
    {
        var visibility = new WindowVisibilityCoordinator();
        visibility.SetUserRequestedVisibility(false);
        var host = new FakeHost();
        var service = new SettingsWindowService(() => host);

        service.Open();

        Assert.False(visibility.State.IsActuallyVisible);
        Assert.True(service.IsOpen);
        Assert.Equal(1, host.ShowCount);
    }

    private sealed class FakeHost : ISettingsWindowHost
    {
        public bool IsMinimized { get; set; }
        public event EventHandler? Closed;
        internal int ShowCount { get; private set; }
        internal int ActivateCount { get; private set; }
        internal int RestoreCount { get; private set; }
        public void Show() => ShowCount++;
        public void Activate() => ActivateCount++;
        public void Restore() { RestoreCount++; IsMinimized = false; }
        public void Close() => Closed?.Invoke(this, EventArgs.Empty);
    }
}
