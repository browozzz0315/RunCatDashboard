using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using RunCatDashboard.App.Windowing;

namespace RunCatDashboard.App.Settings;

internal interface ISettingsFileSystem
{
    bool FileExists(string path);
    Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken);
    Stream CreateWriteStream(string path);
    void MoveFile(string source, string destination, bool overwrite);
    void ReplaceFile(string source, string destination);
    void DeleteFile(string path);
    void CreateDirectory(string path);
    IEnumerable<string> EnumerateFiles(string directory, string pattern);
    DateTime GetLastWriteTimeUtc(string path);
}

internal sealed class PhysicalSettingsFileSystem : ISettingsFileSystem
{
    public bool FileExists(string path) => File.Exists(path);
    public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken) =>
        File.ReadAllTextAsync(path, cancellationToken);
    public Stream CreateWriteStream(string path) => new FileStream(
        path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096,
        FileOptions.Asynchronous | FileOptions.WriteThrough);
    public void MoveFile(string source, string destination, bool overwrite) =>
        File.Move(source, destination, overwrite);
    public void ReplaceFile(string source, string destination) =>
        File.Replace(source, destination, null);
    public void DeleteFile(string path) => File.Delete(path);
    public void CreateDirectory(string path) => Directory.CreateDirectory(path);
    public IEnumerable<string> EnumerateFiles(string directory, string pattern) =>
        Directory.EnumerateFiles(directory, pattern);
    public DateTime GetLastWriteTimeUtc(string path) => File.GetLastWriteTimeUtc(path);
}

internal sealed class JsonSettingsStore : ISettingsStore
{
    internal const string SettingsFileName = "settings.json";
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    static JsonSettingsStore()
    {
        SerializerOptions.Converters.Add(new LenientInteractionModeConverter());
        SerializerOptions.Converters.Add(new LenientOverlaySizeModeConverter());
        SerializerOptions.Converters.Add(new LenientOverlayHotKeyKeyConverter());
        SerializerOptions.Converters.Add(new LenientThemePreferenceConverter());
        SerializerOptions.Converters.Add(new LenientAnimationSpeedPreferenceConverter());
    }

    private readonly string _directory;
    private readonly string _settingsPath;
    private readonly ISettingsFileSystem _fileSystem;
    private readonly TimeProvider _timeProvider;

    internal JsonSettingsStore(
        string directory,
        ISettingsFileSystem fileSystem,
        TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(fileSystem);
        _directory = directory;
        _settingsPath = Path.Combine(directory, SettingsFileName);
        _fileSystem = fileSystem;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<SettingsLoadResult> LoadAsync(CancellationToken cancellationToken = default)
    {
        CleanupTemporaryFiles();
        if (!_fileSystem.FileExists(_settingsPath))
        {
            return new SettingsLoadResult(AppSettings.Defaults, null);
        }

        try
        {
            string json = await _fileSystem.ReadAllTextAsync(_settingsPath, cancellationToken)
                .ConfigureAwait(false);
            using JsonDocument document = JsonDocument.Parse(json);
            int version = document.RootElement.TryGetProperty("version", out JsonElement element) &&
                element.TryGetInt32(out int parsedVersion)
                    ? parsedVersion
                    : 0;
            if (version is not 1 and not 2 and not 3 and not 4 and not 5 &&
                version != AppSettings.CurrentVersion)
            {
                string backup = BackupInvalidFile($"unsupported-v{version}");
                return new SettingsLoadResult(
                    AppSettings.Defaults,
                    $"不支援設定 schema version {version}；原檔已備份至 {Path.GetFileName(backup)}。");
            }

            AppSettings? settings = JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions);
            return new SettingsLoadResult(AppSettingsValidator.Normalize(settings), null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            string backup;
            try
            {
                backup = BackupInvalidFile("corrupt");
            }
            catch (Exception backupException) when (backupException is not OperationCanceledException)
            {
                return new SettingsLoadResult(
                    AppSettings.Defaults,
                    $"設定檔損壞且無法建立備份，已使用預設值：{backupException.Message}");
            }
            return new SettingsLoadResult(
                AppSettings.Defaults,
                $"設定檔損壞；原檔已備份至 {Path.GetFileName(backup)}：{exception.Message}");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new SettingsLoadResult(
                AppSettings.Defaults,
                $"讀取設定檔失敗，已使用預設值：{exception.Message}");
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        using IPreparedSettingsWrite prepared =
            await PrepareSaveAsync(settings, cancellationToken).ConfigureAwait(false);
        prepared.Commit();
    }

    public async Task<IPreparedSettingsWrite> PrepareSaveAsync(
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _fileSystem.CreateDirectory(_directory);
        string temporaryPath = Path.Combine(
            _directory,
            $".{SettingsFileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (Stream stream = _fileSystem.CreateWriteStream(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    AppSettingsValidator.Normalize(settings),
                    SerializerOptions,
                    cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                if (stream is FileStream fileStream)
                {
                    fileStream.Flush(flushToDisk: true);
                }
            }

            return new PreparedSettingsWrite(
                _fileSystem,
                temporaryPath,
                _settingsPath);
        }
        catch
        {
            if (_fileSystem.FileExists(temporaryPath))
            {
                try
                {
                    _fileSystem.DeleteFile(temporaryPath);
                }
                catch
                {
                    // The write diagnostic is more useful than a cleanup exception.
                }
            }
            throw;
        }
    }

    private string BackupInvalidFile(string category)
    {
        _fileSystem.CreateDirectory(_directory);
        string timestamp = _timeProvider.GetUtcNow().ToString(
            "yyyyMMdd-HHmmss-fffffff",
            CultureInfo.InvariantCulture);
        string backupPath = Path.Combine(
            _directory,
            $"settings.{category}-{timestamp}.json");
        _fileSystem.MoveFile(_settingsPath, backupPath, overwrite: false);
        PruneInvalidBackups();
        return backupPath;
    }

    private void PruneInvalidBackups()
    {
        string[] backups = _fileSystem
            .EnumerateFiles(_directory, "settings.*-*.json")
            .Where(path =>
                Path.GetFileName(path).StartsWith("settings.corrupt-", StringComparison.Ordinal) ||
                Path.GetFileName(path).StartsWith("settings.unsupported-v", StringComparison.Ordinal))
            .OrderByDescending(_fileSystem.GetLastWriteTimeUtc)
            .ThenByDescending(path => path, StringComparer.Ordinal)
            .ToArray();
        foreach (string staleBackup in backups.Skip(3))
        {
            _fileSystem.DeleteFile(staleBackup);
        }
    }

    private void CleanupTemporaryFiles()
    {
        string[] temporaryFiles;
        try
        {
            temporaryFiles = _fileSystem
                .EnumerateFiles(_directory, $".{SettingsFileName}.*.tmp")
                .ToArray();
        }
        catch (DirectoryNotFoundException)
        {
            return;
        }
        foreach (string temporaryFile in temporaryFiles)
        {
            try { _fileSystem.DeleteFile(temporaryFile); } catch { }
        }
    }

    private sealed class LenientInteractionModeConverter : JsonConverter<OverlayInteractionMode>
    {
        public override OverlayInteractionMode Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String &&
                Enum.TryParse(reader.GetString(), ignoreCase: true, out OverlayInteractionMode mode))
            {
                return mode;
            }
            if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out int numeric))
            {
                return (OverlayInteractionMode)numeric;
            }
            if (reader.TokenType == JsonTokenType.String)
            {
                return (OverlayInteractionMode)(-1);
            }
            throw new JsonException("interactionMode must be a string or integer.");
        }

        public override void Write(
            Utf8JsonWriter writer,
            OverlayInteractionMode value,
            JsonSerializerOptions options) => writer.WriteStringValue(value.ToString());
    }

    private sealed class LenientOverlaySizeModeConverter : JsonConverter<OverlaySizeMode>
    {
        public override OverlaySizeMode Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String &&
                Enum.TryParse(reader.GetString(), ignoreCase: true, out OverlaySizeMode mode))
            {
                return mode;
            }
            if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out int numeric))
            {
                return (OverlaySizeMode)numeric;
            }
            if (reader.TokenType == JsonTokenType.String)
            {
                return (OverlaySizeMode)(-1);
            }
            throw new JsonException("sizeMode must be a string or integer.");
        }

        public override void Write(
            Utf8JsonWriter writer,
            OverlaySizeMode value,
            JsonSerializerOptions options) => writer.WriteStringValue(value.ToString());
    }

    private sealed class LenientOverlayHotKeyKeyConverter : JsonConverter<OverlayHotKeyKey>
    {
        public override OverlayHotKeyKey Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String &&
                Enum.TryParse(reader.GetString(), ignoreCase: true, out OverlayHotKeyKey key))
            {
                return key;
            }
            if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out int numeric))
            {
                return (OverlayHotKeyKey)numeric;
            }
            if (reader.TokenType == JsonTokenType.String)
            {
                return (OverlayHotKeyKey)(-1);
            }
            throw new JsonException("interactionHotKey.key must be a string or integer.");
        }

        public override void Write(
            Utf8JsonWriter writer,
            OverlayHotKeyKey value,
            JsonSerializerOptions options) => writer.WriteStringValue(value.ToString());
    }

    private sealed class LenientThemePreferenceConverter : JsonConverter<ThemePreference>
    {
        public override ThemePreference Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String &&
                Enum.TryParse(reader.GetString(), ignoreCase: true, out ThemePreference preference))
            {
                return preference;
            }

            if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out int numeric))
            {
                return (ThemePreference)numeric;
            }

            if (reader.TokenType == JsonTokenType.String)
            {
                return (ThemePreference)(-1);
            }

            return (ThemePreference)(-1);
        }

        public override void Write(
            Utf8JsonWriter writer,
            ThemePreference value,
            JsonSerializerOptions options) => writer.WriteStringValue(value.ToString());
    }

    private sealed class LenientAnimationSpeedPreferenceConverter
        : JsonConverter<AnimationSpeedPreference>
    {
        public override AnimationSpeedPreference Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String &&
                Enum.TryParse(reader.GetString(), ignoreCase: true,
                    out AnimationSpeedPreference preference))
            {
                return preference;
            }

            if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out int numeric))
            {
                return (AnimationSpeedPreference)numeric;
            }

            return (AnimationSpeedPreference)(-1);
        }

        public override void Write(
            Utf8JsonWriter writer,
            AnimationSpeedPreference value,
            JsonSerializerOptions options) => writer.WriteStringValue(value.ToString());
    }

    private sealed class PreparedSettingsWrite(
        ISettingsFileSystem fileSystem,
        string temporaryPath,
        string settingsPath) : IPreparedSettingsWrite
    {
        private bool _committed;
        private bool _disposed;

        public void Commit()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_committed)
            {
                return;
            }

            if (fileSystem.FileExists(settingsPath))
            {
                fileSystem.ReplaceFile(temporaryPath, settingsPath);
            }
            else
            {
                fileSystem.MoveFile(temporaryPath, settingsPath, overwrite: false);
            }
            _committed = true;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            if (_committed || !fileSystem.FileExists(temporaryPath))
            {
                return;
            }

            try
            {
                fileSystem.DeleteFile(temporaryPath);
            }
            catch
            {
                // The write diagnostic is more useful than a cleanup exception.
            }
        }
    }
}
