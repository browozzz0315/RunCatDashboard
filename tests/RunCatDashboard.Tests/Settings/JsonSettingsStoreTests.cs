using System.Text.Json;
using System.IO;
using RunCatDashboard.App.Interop;
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
    public async Task SchemaV2_RoundTripsAllContractFields()
    {
        var expected = new AppSettings(
            2,
            new WindowSettings(-420.5, 18.25, false),
            new OverlaySettings(
                OverlayInteractionMode.Interactive,
                new OverlayHotKeyGesture(true, false, true, true, OverlayHotKeyKey.F12)),
            new MetricsSettings(5000),
            new StartupSettings(true));
        var store = CreateStore();

        await store.SaveAsync(expected);
        SettingsLoadResult result = await store.LoadAsync();

        Assert.Equal(expected, result.Settings);
        string json = await File.ReadAllTextAsync(Path.Combine(_directory, "settings.json"));
        Assert.Contains("\"version\": 2", json);
        Assert.Contains("\"interactionHotKey\"", json);
        Assert.Contains("\"key\": \"F12\"", json);
        Assert.DoesNotContain("width", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fullscreen", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SchemaV1_MigratesWithoutLosingExistingValuesAndUsesDefaultHotKey()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(Path.Combine(_directory, "settings.json"), """
            {
              "version": 1,
              "window": { "left": -420.5, "top": 18.25, "isDashboardVisible": false },
              "overlay": { "interactionMode": "Interactive" },
              "metrics": { "samplingIntervalMilliseconds": 5000 },
              "startup": { "runAtLoginRequested": true }
            }
            """);

        SettingsLoadResult result = await CreateStore().LoadAsync();

        Assert.Equal(2, result.Settings.Version);
        Assert.Equal(new WindowSettings(-420.5, 18.25, false), result.Settings.Window);
        Assert.Equal(OverlayInteractionMode.Interactive, result.Settings.Overlay.InteractionMode);
        Assert.Equal(OverlayHotKeyGesture.Default, result.Settings.Overlay.InteractionHotKey);
        Assert.Equal(5000, result.Settings.Metrics.SamplingIntervalMilliseconds);
        Assert.True(result.Settings.Startup.RunAtLoginRequested);
        Assert.Null(result.Diagnostic);
    }

    [Fact]
    public async Task SchemaV2_ColdStartRegistersPersistedHotKeyInsteadOfDefault()
    {
        var persisted = new OverlayHotKeyGesture(
            false, true, true, true, OverlayHotKeyKey.F8);
        await CreateStore().SaveAsync(AppSettings.Defaults with
        {
            Overlay = AppSettings.Defaults.Overlay with
            {
                InteractionHotKey = persisted
            }
        });
        SettingsLoadResult loaded = await CreateStore().LoadAsync();
        var native = new RecordingGlobalHotKeyApi();
        using var controller = new GlobalHotKeyController(
            native,
            initialInteractionGesture: loaded.Settings.Overlay.InteractionHotKey);

        controller.RegisterAll(new nint(1234));

        Assert.Contains(native.Registrations, registration =>
            registration.Identifier == GlobalHotKeyController.InteractionHotKeyIdentifier &&
            registration.VirtualKey == (uint)persisted.Key);
        Assert.DoesNotContain(native.Registrations, registration =>
            registration.Identifier == GlobalHotKeyController.InteractionHotKeyIdentifier &&
            registration.VirtualKey == (uint)OverlayHotKeyGesture.Default.Key);
    }

    [Fact]
    public void Normalize_PreservesValidCustomHotKey()
    {
        var gesture = new OverlayHotKeyGesture(
            true, false, true, true, OverlayHotKeyKey.D7);
        AppSettings settings = AppSettings.Defaults with
        {
            Overlay = AppSettings.Defaults.Overlay with
            {
                InteractionHotKey = gesture
            }
        };

        AppSettings normalized = AppSettingsValidator.Normalize(settings);

        Assert.Equal(gesture, normalized.Overlay.InteractionHotKey);
    }

    [Fact]
    public async Task InvalidSavedHotKey_UsesDefaultWithoutDiscardingOtherSettings()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(Path.Combine(_directory, "settings.json"), """
            {
              "version": 2,
              "window": { "isDashboardVisible": false },
              "overlay": {
                "interactionMode": "Interactive",
                "interactionHotKey": {
                  "control": false,
                  "alt": false,
                  "shift": false,
                  "windows": false,
                  "key": "A"
                }
              },
              "metrics": { "samplingIntervalMilliseconds": 500 },
              "startup": { "runAtLoginRequested": true }
            }
            """);

        SettingsLoadResult result = await CreateStore().LoadAsync();

        Assert.Equal(OverlayHotKeyGesture.Default, result.Settings.Overlay.InteractionHotKey);
        Assert.False(result.Settings.Window.IsDashboardVisible);
        Assert.Equal(OverlayInteractionMode.Interactive, result.Settings.Overlay.InteractionMode);
        Assert.Equal(500, result.Settings.Metrics.SamplingIntervalMilliseconds);
        Assert.True(result.Settings.Startup.RunAtLoginRequested);
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

    private sealed class RecordingGlobalHotKeyApi : INativeGlobalHotKeyApi
    {
        internal List<(int Identifier, uint Modifiers, uint VirtualKey)> Registrations { get; } = [];

        public void Register(nint windowHandle, int identifier, uint modifiers, uint virtualKey) =>
            Registrations.Add((identifier, modifiers, virtualKey));

        public void Unregister(nint windowHandle, int identifier)
        {
        }
    }

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
