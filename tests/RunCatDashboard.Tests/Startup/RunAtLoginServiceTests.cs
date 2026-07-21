using RunCatDashboard.App.Startup;

namespace RunCatDashboard.Tests.Startup;

public sealed class RunAtLoginServiceTests
{
    private const string CurrentPath = @"C:\Apps\RunCatDashboard.exe";

    [Fact]
    public async Task Enable_WritesQuotedCommandAndIsIdempotent()
    {
        var registry = new FakeRegistry();
        var service = new RunAtLoginService(registry, () => CurrentPath);

        RunAtLoginState first = await service.ReconcileAsync(true);
        RunAtLoginState second = await service.ReconcileAsync(true);

        Assert.True(first.Applied);
        Assert.True(second.Applied);
        Assert.Equal("\"C:\\Apps\\RunCatDashboard.exe\"", registry.Values[RunAtLoginService.ValueName]);
        Assert.Equal(1, registry.WriteCount);
    }

    [Fact]
    public async Task Enable_ReconcilesStaleExecutablePath()
    {
        var registry = new FakeRegistry();
        registry.Values[RunAtLoginService.ValueName] = "\"C:\\Old\\RunCatDashboard.exe\"";
        var service = new RunAtLoginService(registry, () => CurrentPath);

        RunAtLoginState state = await service.ReconcileAsync(true);

        Assert.True(state.Applied);
        Assert.Equal("\"C:\\Apps\\RunCatDashboard.exe\"", registry.Values[RunAtLoginService.ValueName]);
    }

    [Fact]
    public async Task Disable_DeletesOnlyRunCatDashboardValue()
    {
        var registry = new FakeRegistry();
        registry.Values[RunAtLoginService.ValueName] = "ours";
        registry.Values["OtherApp"] = "keep";
        var service = new RunAtLoginService(registry, () => CurrentPath);

        RunAtLoginState state = await service.ReconcileAsync(false);

        Assert.False(state.Applied);
        Assert.False(registry.Values.ContainsKey(RunAtLoginService.ValueName));
        Assert.Equal("keep", registry.Values["OtherApp"]);
    }

    [Fact]
    public async Task InvalidProcessPath_NeverWritesIncorrectCommand()
    {
        var registry = new FakeRegistry();
        var service = new RunAtLoginService(registry, () => @"C:\dotnet.exe");

        RunAtLoginState state = await service.ReconcileAsync(true);

        Assert.True(state.Requested);
        Assert.False(state.Applied);
        Assert.NotNull(state.Fault);
        Assert.Equal(0, registry.WriteCount);
    }

    [Fact]
    public async Task RegistryFailure_PreservesRequestedAppliedAndFault()
    {
        var registry = new FakeRegistry { WriteException = new UnauthorizedAccessException("denied") };
        var service = new RunAtLoginService(registry, () => CurrentPath);

        RunAtLoginState state = await service.ReconcileAsync(true);

        Assert.True(state.Requested);
        Assert.False(state.Applied);
        Assert.Contains("denied", state.Fault);
    }

    private sealed class FakeRegistry : IRunRegistry
    {
        internal Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);
        internal Exception? WriteException { get; init; }
        internal int WriteCount { get; private set; }
        public string? Read(string valueName) => Values.GetValueOrDefault(valueName);
        public void Write(string valueName, string value)
        {
            if (WriteException is not null) throw WriteException;
            WriteCount++;
            Values[valueName] = value;
        }
        public void Delete(string valueName) => Values.Remove(valueName);
    }
}
