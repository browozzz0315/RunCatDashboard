using System.Text.Json;
using System.IO;
using RunCatDashboard.App.Settings;
using RunCatDashboard.App.Windowing;

namespace RunCatDashboard.Tests.Settings;

public sealed class JsonSettingsStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "RunCatDashboard.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task MissingFile_ReturnsDefaults()
    {
        var store = CreateStore();

        SettingsLoadResult result = await store.LoadAsync();

        Assert.Equal(AppSettings.Defaults, result.Settings);
        Assert.Null(result.Diagnostic);
    }

    [Fact]
    public async Task SchemaV1_RoundTripsAllContractFields()
    {
        var expected = new AppSettings(
            1,
            new WindowSettings(-420.5, 18.25, false),
            new OverlaySettings(OverlayInteractionMode.Interactive),
            new MetricsSettings(5000),
            new StartupSettings(true));
        var store = CreateStore();

        await store.SaveAsync(expected);
        SettingsLoadResult result = await store.LoadAsync();

        Assert.Equal(expected, result.Settings);
        string json = await File.ReadAllTextAsync(Path.Combine(_directory, "settings.json"));
        Assert.Contains("\"version\": 1", json);
        Assert.DoesNotContain("width", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fullscreen", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnknownFieldsAreIgnored_AndInvalidValuesUseSpecifiedFallbacks()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(Path.Combine(_directory, "settings.json"), """
            {
              "version": 1,
              "future": { "value": 42 },
              "window": { "left": -100, "top": null, "isDashboardVisible": false },
              "overlay": { "interactionMode": "FutureMode" },
              "metrics": { "samplingIntervalMilliseconds": 777 },
              "startup": { "runAtLoginRequested": true }
            }
            """);

        SettingsLoadResult result = await CreateStore().LoadAsync();

        Assert.Null(result.Settings.Window.Left);
        Assert.Null(result.Settings.Window.Top);
        Assert.False(result.Settings.Window.IsDashboardVisible);
        Assert.Equal(OverlayInteractionMode.ClickThrough, result.Settings.Overlay.InteractionMode);
        Assert.Equal(1000, result.Settings.Metrics.SamplingIntervalMilliseconds);
        Assert.True(result.Settings.Startup.RunAtLoginRequested);
    }

    [Theory]
    [InlineData("{ bad json", "settings.corrupt-")]
    [InlineData("{ \"version\": 9 }", "settings.unsupported-v9-")]
    public async Task MalformedOrUnsupported_IsBackedUpAndUsesDefaults(
        string json,
        string backupPrefix)
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(Path.Combine(_directory, "settings.json"), json);

        SettingsLoadResult result = await CreateStore().LoadAsync();

        Assert.Equal(AppSettings.Defaults, result.Settings);
        Assert.NotNull(result.Diagnostic);
        Assert.False(File.Exists(Path.Combine(_directory, "settings.json")));
        Assert.Single(Directory.EnumerateFiles(_directory, $"{backupPrefix}*.json"));
    }

    [Fact]
    public async Task InvalidBackups_ArePrunedToNewestThreeCombined()
    {
        Directory.CreateDirectory(_directory);
        for (int index = 0; index < 5; index++)
        {
            await File.WriteAllTextAsync(
                Path.Combine(_directory, "settings.json"),
                index % 2 == 0 ? "bad" : $"{{ \"version\": {index + 2} }}");
            await CreateStore().LoadAsync();
            await Task.Delay(20);
        }

        Assert.Equal(3, Directory.EnumerateFiles(_directory, "settings.*-*.json").Count());
    }

    [Fact]
    public async Task AtomicReplaceFailure_LeavesOldFileIntactAndCleansTemp()
    {
        Directory.CreateDirectory(_directory);
        string settingsPath = Path.Combine(_directory, "settings.json");
        const string original = "{ \"version\": 1, \"window\": null }";
        await File.WriteAllTextAsync(settingsPath, original);
        var fileSystem = new ReplaceFailingFileSystem();
        var store = new JsonSettingsStore(_directory, fileSystem);

        await Assert.ThrowsAsync<IOException>(() => store.SaveAsync(AppSettings.Defaults));

        Assert.Equal(original, await File.ReadAllTextAsync(settingsPath));
        Assert.Empty(Directory.EnumerateFiles(_directory, "*.tmp"));
    }

    [Fact]
    public async Task Load_CleansStaleSameDirectoryTemporaryFiles()
    {
        Directory.CreateDirectory(_directory);
        string stale = Path.Combine(_directory, ".settings.json.abcd.tmp");
        await File.WriteAllTextAsync(stale, "partial");

        SettingsLoadResult result = await CreateStore().LoadAsync();

        Assert.Equal(AppSettings.Defaults, result.Settings);
        Assert.False(File.Exists(stale));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private JsonSettingsStore CreateStore() =>
        new(_directory, new PhysicalSettingsFileSystem());

    private sealed class ReplaceFailingFileSystem : ISettingsFileSystem
    {
        private readonly PhysicalSettingsFileSystem _inner = new();
        public bool FileExists(string path) => _inner.FileExists(path);
        public Task<string> ReadAllTextAsync(string path, CancellationToken token) =>
            _inner.ReadAllTextAsync(path, token);
        public Stream CreateWriteStream(string path) => _inner.CreateWriteStream(path);
        public void MoveFile(string source, string destination, bool overwrite) =>
            _inner.MoveFile(source, destination, overwrite);
        public void ReplaceFile(string source, string destination) =>
            throw new IOException("configured replace failure");
        public void DeleteFile(string path) => _inner.DeleteFile(path);
        public void CreateDirectory(string path) => _inner.CreateDirectory(path);
        public IEnumerable<string> EnumerateFiles(string directory, string pattern) =>
            _inner.EnumerateFiles(directory, pattern);
        public DateTime GetLastWriteTimeUtc(string path) => _inner.GetLastWriteTimeUtc(path);
    }
}
