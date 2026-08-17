using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging;
using RunCatDashboard.App.Diagnostics;
using RunCatDashboard.App.Settings;
using RunCatDashboard.App.Theming;

namespace RunCatDashboard.App.Animation;

internal sealed class AnimationValidationException : InvalidOperationException
{
    internal AnimationValidationException(string message)
        : base(message)
    {
    }

    internal AnimationValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

internal sealed record AnimationManifest(
    int FormatVersion,
    string AnimationId,
    string DisplayName,
    string Format,
    int FrameCount,
    int FrameWidth,
    int FrameHeight,
    int BaseFrameIntervalMilliseconds);

public sealed record AnimationCatalogEntry(
    string Id,
    string DisplayName,
    bool IsBuiltIn,
    bool IsValid,
    int FrameCount,
    int FrameWidth,
    int FrameHeight,
    int BaseFrameIntervalMilliseconds,
    string? PhysicalDirectory,
    string? ValidationError = null)
{
    public string DisplayLabel => IsBuiltIn
        ? $"{DisplayName}（內建）"
        : $"{DisplayName}（自訂）";

    public override string ToString() => DisplayLabel;
}

internal sealed record AnimationResolution(
    AnimationCatalogEntry Entry,
    bool UsedFallback,
    string? DiagnosticCategory,
    string? Diagnostic);

internal sealed record AnimationImportPreview(
    string SourceFileName,
    int SourceWidth,
    int SourceHeight,
    int FrameCount,
    int FrameWidth,
    int FrameHeight,
    IReadOnlyList<BitmapSource> Frames);

internal sealed class SpriteSheetParser
{
    internal const int MinimumFrameCount = 1;
    internal const int MaximumFrameCount = 64;
    internal const int MaximumSourceDimension = 4096;
    internal const int MaximumFrameDimension = 1024;
    internal const long MaximumDecodedPixelBudget = 8_388_608;

    internal AnimationImportPreview Parse(string sourcePath, int frameCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        if (!string.Equals(Path.GetExtension(sourcePath), ".png", StringComparison.OrdinalIgnoreCase))
        {
            throw new AnimationValidationException("自訂動畫只支援 PNG 檔案。\n請選擇副檔名為 .png 的檔案。");
        }

        if (frameCount is < MinimumFrameCount or > MaximumFrameCount)
        {
            throw new AnimationValidationException(
                $"幀數必須介於 {MinimumFrameCount} 至 {MaximumFrameCount} 之間。");
        }

        BitmapSource source = DecodePng(sourcePath);
        int width = source.PixelWidth;
        int height = source.PixelHeight;
        if (width > MaximumSourceDimension || height > MaximumSourceDimension)
        {
            throw new AnimationValidationException(
                $"來源圖片尺寸不可超過 {MaximumSourceDimension} x {MaximumSourceDimension} 像素。");
        }

        if ((long)width * height > MaximumDecodedPixelBudget)
        {
            throw new AnimationValidationException(
                $"來源圖片解碼像素總量不可超過 {MaximumDecodedPixelBudget:N0}。");
        }

        if (width % frameCount != 0)
        {
            throw new AnimationValidationException("來源圖片寬度必須可被幀數整除。");
        }

        int frameWidth = width / frameCount;
        if (frameWidth > MaximumFrameDimension || height > MaximumFrameDimension)
        {
            throw new AnimationValidationException(
                $"每幀尺寸不可超過 {MaximumFrameDimension} x {MaximumFrameDimension} 像素。");
        }

        var frames = new BitmapSource[frameCount];
        for (int index = 0; index < frameCount; index++)
        {
            var crop = new CroppedBitmap(
                source,
                new System.Windows.Int32Rect(index * frameWidth, 0, frameWidth, height));
            crop.Freeze();
            frames[index] = crop;
        }

        return new AnimationImportPreview(
            Path.GetFileName(sourcePath),
            width,
            height,
            frameCount,
            frameWidth,
            height,
            Array.AsReadOnly(frames));
    }

    internal static BitmapSource DecodePng(string path)
    {
        try
        {
            using FileStream stream = new(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                4096,
                FileOptions.SequentialScan);
            var decoder = new PngBitmapDecoder(
                stream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count != 1)
            {
                throw new AnimationValidationException("PNG 必須是單一影像，不支援 APNG 或多影格內容。");
            }

            BitmapFrame frame = decoder.Frames[0];
            frame.Freeze();
            return frame;
        }
        catch (AnimationValidationException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new AnimationValidationException(
                "PNG 內容無法解碼，請選擇有效的 PNG 檔案。",
                exception);
        }
    }
}

internal sealed class AnimationLibraryStorage
{
    internal const string ManifestFileName = "manifest.json";
    internal const string FrameFilePattern = "frame-*.png";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string _animationsDirectory;

    internal AnimationLibraryStorage(string animationsDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(animationsDirectory);
        _animationsDirectory = Path.GetFullPath(animationsDirectory);
    }

    internal string AnimationsDirectory => _animationsDirectory;

    internal void CleanupStaleImportDirectories(ILogger? logger = null)
    {
        if (!Directory.Exists(_animationsDirectory))
        {
            return;
        }

        string[] directories;
        try
        {
            directories = Directory
                .EnumerateDirectories(_animationsDirectory, ".import-*.tmp")
                .ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            TryLog(logger, () => logger!.LogWarning(
                exception,
                "Stale animation import enumeration failed. {Operation} {Subsystem} {FaultState}",
                "CleanupStaleAnimationImport",
                "Animation",
                "Faulted"));
            return;
        }

        foreach (string directory in directories)
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                TryLog(logger, () => logger!.LogWarning(
                    exception,
                    "Stale animation import cleanup failed. {Operation} {Subsystem} {FaultState}",
                    "CleanupStaleAnimationImport",
                    "Animation",
                    "Faulted"));
            }
        }
    }

    internal string CreateTemporaryDirectory()
    {
        Directory.CreateDirectory(_animationsDirectory);
        string path = Path.Combine(_animationsDirectory, $".import-{Guid.NewGuid():N}.tmp");
        Directory.CreateDirectory(path);
        return path;
    }

    internal void WriteAnimation(
        string temporaryDirectory,
        AnimationManifest manifest,
        IReadOnlyList<BitmapSource> frames)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(frames);
        if (frames.Count != manifest.FrameCount)
        {
            throw new AnimationValidationException("寫入的幀數與 manifest 不一致。");
        }

        Directory.CreateDirectory(temporaryDirectory);
        string manifestPath = Path.Combine(temporaryDirectory, ManifestFileName);
        File.WriteAllText(
            manifestPath,
            JsonSerializer.Serialize(manifest, SerializerOptions),
            new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        for (int index = 0; index < frames.Count; index++)
        {
            string path = Path.Combine(temporaryDirectory, GetFrameFileName(index));
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(frames[index]));
            encoder.Save(stream);
        }
    }

    internal void Publish(string temporaryDirectory, string animationId)
    {
        ValidateChildDirectory(temporaryDirectory);
        if (!IsValidAnimationId(animationId))
        {
            throw new AnimationValidationException("自訂動畫 ID 無效。");
        }

        string destination = Path.Combine(_animationsDirectory, animationId);
        if (Directory.Exists(destination) || File.Exists(destination))
        {
            throw new IOException("相同的自訂動畫 ID 已存在，未覆寫原有資料。");
        }

        Directory.Move(temporaryDirectory, destination);
    }

    internal void Delete(string animationId)
    {
        if (!IsValidAnimationId(animationId))
        {
            throw new AnimationValidationException("自訂動畫 ID 無效。");
        }

        string directory = Path.Combine(_animationsDirectory, animationId);
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    internal AnimationManifest ReadManifest(string directory)
    {
        string json = File.ReadAllText(Path.Combine(directory, ManifestFileName));
        AnimationManifest? manifest = JsonSerializer.Deserialize<AnimationManifest>(json, SerializerOptions);
        return manifest ?? throw new AnimationValidationException("manifest.json 為空。");
    }

    internal IReadOnlyList<BitmapSource> LoadAndValidateFrames(
        string directory,
        AnimationManifest manifest)
    {
        ValidateManifest(manifest, Path.GetFileName(directory));
        string[] files = Directory
            .EnumerateFiles(directory, "*.png", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(name => name is not null)
            .Cast<string>()
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        string[] expected = Enumerable.Range(0, manifest.FrameCount)
            .Select(GetFrameFileName)
            .ToArray();
        if (!files.SequenceEqual(expected, StringComparer.Ordinal))
        {
            throw new AnimationValidationException("動畫 frame 檔案序列不符合 normalized contract。");
        }

        var frames = new BitmapSource[manifest.FrameCount];
        for (int index = 0; index < frames.Length; index++)
        {
            BitmapSource frame = SpriteSheetParser.DecodePng(Path.Combine(directory, expected[index]));
            if (frame.PixelWidth != manifest.FrameWidth || frame.PixelHeight != manifest.FrameHeight)
            {
                throw new AnimationValidationException("manifest 尺寸與 frame 尺寸不一致。");
            }

            frames[index] = frame;
        }

        if ((long)manifest.FrameWidth * manifest.FrameHeight * manifest.FrameCount >
            SpriteSheetParser.MaximumDecodedPixelBudget)
        {
            throw new AnimationValidationException("動畫解碼像素總量超過 V1 限制。");
        }

        return Array.AsReadOnly(frames);
    }

    internal static string GetFrameFileName(int index) => $"frame-{index:D3}.png";

    internal static bool IsValidAnimationId(string? animationId) =>
        animationId is not null &&
        animationId.StartsWith("custom-", StringComparison.Ordinal) &&
        animationId.Length == "custom-".Length + 32 &&
        animationId[7..].All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');

    internal static void ValidateManifest(AnimationManifest manifest, string physicalDirectoryName)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.FormatVersion != AnimationSettings.CurrentFormatVersion)
            throw new AnimationValidationException("不支援的動畫 manifest format version。");
        bool isTemporaryDirectory = physicalDirectoryName.StartsWith(".import-", StringComparison.Ordinal);
        if (!IsValidAnimationId(manifest.AnimationId) ||
            (!isTemporaryDirectory &&
             !string.Equals(manifest.AnimationId, physicalDirectoryName, StringComparison.Ordinal)))
            throw new AnimationValidationException("manifest animationId 與實體資料夾不一致。");
        if (string.IsNullOrWhiteSpace(manifest.DisplayName))
            throw new AnimationValidationException("manifest displayName 不可為空白。");
        if (!string.Equals(manifest.Format, "png", StringComparison.OrdinalIgnoreCase))
            throw new AnimationValidationException("動畫 format 必須為 png。");
        if (manifest.FrameCount is < SpriteSheetParser.MinimumFrameCount or > SpriteSheetParser.MaximumFrameCount)
            throw new AnimationValidationException("manifest frameCount 超出 V1 限制。");
        if (manifest.FrameWidth is < 1 or > SpriteSheetParser.MaximumFrameDimension ||
            manifest.FrameHeight is < 1 or > SpriteSheetParser.MaximumFrameDimension)
            throw new AnimationValidationException("manifest frame 尺寸超出 V1 限制。");
        if (manifest.BaseFrameIntervalMilliseconds != 250)
            throw new AnimationValidationException("manifest baseFrameIntervalMilliseconds 必須為 250。");
    }

    private void ValidateChildDirectory(string directory)
    {
        string full = Path.GetFullPath(directory);
        string parent = Path.GetFullPath(_animationsDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        if (!full.StartsWith(parent, StringComparison.OrdinalIgnoreCase) ||
            !Directory.Exists(full))
        {
            throw new IOException("動畫 temporary directory 不在受控資料夾內。");
        }
    }

    private static void TryLog(ILogger? logger, Action log)
    {
        if (logger is null) return;
        try { log(); } catch { }
    }
}

internal sealed class AnimationCatalog
{
    private readonly AnimationLibraryStorage _storage;
    private readonly ILogger<AnimationCatalog> _logger;
    private readonly List<AnimationCatalogEntry> _entries = [];
    private bool _isRefreshed;

    internal AnimationCatalog(
        AnimationLibraryStorage storage,
        ILogger<AnimationCatalog>? logger = null)
    {
        _storage = storage;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<AnimationCatalog>.Instance;
    }

    internal IReadOnlyList<AnimationCatalogEntry> Entries
    {
        get
        {
            if (!_isRefreshed) Refresh();
            return _entries.AsReadOnly();
        }
    }

    internal AnimationCatalogEntry BuiltInDefault => Entries[0];

    internal void Refresh()
    {
        _storage.CleanupStaleImportDirectories(_logger);
        _entries.Clear();
        _entries.Add(new AnimationCatalogEntry(
            AnimationSettings.BuiltInDefaultAnimationId,
            "Cat-2 Run",
            true,
            true,
            RunCatAnimationController.DefaultFrameCount,
            50,
            50,
            250,
            null));

        try
        {
            if (Directory.Exists(_storage.AnimationsDirectory))
            {
                foreach (string directory in Directory.EnumerateDirectories(_storage.AnimationsDirectory))
                {
                string name = Path.GetFileName(directory);
                if (name.StartsWith(".import-", StringComparison.Ordinal))
                    continue;

                try
                {
                    AnimationManifest manifest = _storage.ReadManifest(directory);
                    AnimationLibraryStorage.ValidateManifest(manifest, name);
                    _ = _storage.LoadAndValidateFrames(directory, manifest);
                    _entries.Add(new AnimationCatalogEntry(
                        manifest.AnimationId,
                        manifest.DisplayName.Trim(),
                        false,
                        true,
                        manifest.FrameCount,
                        manifest.FrameWidth,
                        manifest.FrameHeight,
                        manifest.BaseFrameIntervalMilliseconds,
                        directory));
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException or JsonException or
                    AnimationValidationException or NotSupportedException)
                {
                    TryLog(() => _logger.LogWarning(
                        exception,
                        "Corrupt custom animation catalog entry. {Operation} {Subsystem} {AnimationId} {FaultState}",
                        "ValidateAnimationCatalogEntry",
                        "Animation",
                        name,
                        "Faulted"));
                    _entries.Add(new AnimationCatalogEntry(
                        name,
                        name,
                        false,
                        false,
                        0,
                        0,
                        0,
                        250,
                        directory,
                        "自訂動畫資料損壞或格式不支援。"));
                }
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            TryLog(() => _logger.LogWarning(
                exception,
                "Animation catalog enumeration failed. {Operation} {Subsystem} {FaultState}",
                "RefreshAnimationCatalog",
                "Animation",
                "Faulted"));
        }

        _entries.Sort((left, right) =>
        {
            if (left.IsBuiltIn) return -1;
            if (right.IsBuiltIn) return 1;
            return StringComparer.OrdinalIgnoreCase.Compare(left.DisplayName, right.DisplayName);
        });
        _isRefreshed = true;
    }

    internal AnimationCatalogEntry? Find(string? animationId) =>
        Entries.FirstOrDefault(entry => string.Equals(entry.Id, animationId, StringComparison.Ordinal));

    internal AnimationResolution Resolve(string? animationId)
    {
        AnimationCatalogEntry builtIn = BuiltInDefault;
        AnimationCatalogEntry? entry = Find(animationId);
        if (entry is { IsBuiltIn: true, IsValid: true })
            return new AnimationResolution(entry, false, null, null);
        if (entry is { IsValid: true })
            return new AnimationResolution(entry, false, null, null);

        string category = entry is null ? "MissingSelectedAnimation" : "CorruptSelectedAnimation";
        string diagnostic = entry is null
            ? "選取的自訂動畫不存在，已改用內建動畫。"
            : "選取的自訂動畫無法使用，已改用內建動畫。";
        return new AnimationResolution(builtIn, true, category, diagnostic);
    }

    internal bool IsDisplayNameAvailable(string displayName, string? exceptId = null)
    {
        string candidate = displayName.Trim();
        return !Entries.Any(entry =>
            !entry.IsBuiltIn &&
            !string.Equals(entry.Id, exceptId, StringComparison.Ordinal) &&
            string.Equals(entry.DisplayName.Trim(), candidate, StringComparison.OrdinalIgnoreCase));
    }

    internal void Delete(AnimationCatalogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.IsBuiltIn)
            throw new AnimationValidationException("內建動畫不可刪除。");
        if (entry.PhysicalDirectory is null || !AnimationLibraryStorage.IsValidAnimationId(entry.Id))
            throw new AnimationValidationException("自訂動畫資料夾無效，未刪除資料。");
        _storage.Delete(entry.Id);
        Refresh();
    }

    private void TryLog(Action action)
    {
        try { action(); } catch { }
    }
}

public interface IRunCatFrameSource
{
    string ActiveAnimationId { get; }
    int FrameCount { get; }
    object? GetFrame(int frameIndex);
    void SetBuiltIn(ResolvedTheme theme);
    void SetCustom(string animationId, IReadOnlyList<BitmapSource> frames);
}

internal sealed class RunCatFrameSource : IRunCatFrameSource
{
    private IReadOnlyList<object> _frames = Array.Empty<object>();

    internal RunCatFrameSource(ResolvedTheme initialTheme = ResolvedTheme.Light)
    {
        SetBuiltIn(initialTheme);
    }

    public string ActiveAnimationId { get; private set; } = AnimationSettings.BuiltInDefaultAnimationId;

    public int FrameCount => _frames.Count;

    public object? GetFrame(int frameIndex) =>
        frameIndex >= 0 && frameIndex < _frames.Count ? _frames[frameIndex] : null;

    public void SetBuiltIn(ResolvedTheme theme)
    {
        _frames = (theme == ResolvedTheme.Dark
                ? RunCatBuiltInFrameProvider.WhiteFrames
                : RunCatBuiltInFrameProvider.BlackFrames)
            .Cast<object>()
            .ToArray();
        ActiveAnimationId = AnimationSettings.BuiltInDefaultAnimationId;
    }

    public void SetCustom(string animationId, IReadOnlyList<BitmapSource> frames)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(animationId);
        ArgumentNullException.ThrowIfNull(frames);
        if (frames.Count == 0)
            throw new AnimationValidationException("自訂動畫沒有可用 frame。");
        _frames = frames.Cast<object>().ToArray();
        ActiveAnimationId = animationId;
    }
}

internal static class RunCatBuiltInFrameProvider
{
    internal static IReadOnlyList<BitmapSource> BlackFrames { get; } = Load(ResolvedTheme.Light);
    internal static IReadOnlyList<BitmapSource> WhiteFrames { get; } = Load(ResolvedTheme.Dark);

    private static IReadOnlyList<BitmapSource> Load(ResolvedTheme theme)
    {
        var frames = new BitmapSource[RunCatAnimationController.DefaultFrameCount];
        string folder = theme == ResolvedTheme.Dark ? "White/" : string.Empty;
        for (int index = 0; index < frames.Length; index++)
        {
            var frame = new BitmapImage();
            frame.BeginInit();
            frame.CacheOption = BitmapCacheOption.OnLoad;
            frame.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
            frame.UriSource = new Uri(
                $"pack://application:,,,/RunCatDashboard;component/Assets/RunCat/{folder}cat-frame-{index + 1:D2}.png",
                UriKind.Absolute);
            frame.EndInit();
            frame.Freeze();
            frames[index] = frame;
        }

        return Array.AsReadOnly(frames);
    }
}

internal sealed class RunCatAnimationRuntime
{
    private readonly AnimationCatalog _catalog;
    private readonly AnimationLibraryStorage _storage;
    private readonly IRunCatFrameSource _frameSource;
    private readonly IRunCatAnimationController _controller;
    private readonly IThemeCoordinator? _themeCoordinator;
    private readonly ILogger<RunCatAnimationRuntime> _logger;

    internal RunCatAnimationRuntime(
        AnimationCatalog catalog,
        AnimationLibraryStorage storage,
        IRunCatFrameSource frameSource,
        IRunCatAnimationController controller,
        IThemeCoordinator? themeCoordinator = null,
        ILogger<RunCatAnimationRuntime>? logger = null)
    {
        _catalog = catalog;
        _storage = storage;
        _frameSource = frameSource;
        _controller = controller;
        _themeCoordinator = themeCoordinator;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<RunCatAnimationRuntime>.Instance;
    }

    internal string ActiveAnimationId => _frameSource.ActiveAnimationId;

    internal AnimationCatalog Catalog => _catalog;

    internal event Action<string?>? DiagnosticChanged;

    internal AnimationResolution Initialize(string selectedAnimationId)
    {
        AnimationResolution resolution = _catalog.Resolve(selectedAnimationId);
        ApplyResolved(resolution.Entry);
        if (resolution.UsedFallback)
        {
            TryLog(() => _logger.LogWarning(
                "Selected animation resolved to built-in fallback. {Operation} {Subsystem} {AnimationId} {ErrorCategory} {FaultState}",
                "ResolveSelectedAnimation",
                "Animation",
                selectedAnimationId,
                resolution.DiagnosticCategory,
                "Fallback"));
            DiagnosticChanged?.Invoke(resolution.Diagnostic);
        }
        else
        {
            DiagnosticChanged?.Invoke(null);
        }

        return resolution;
    }

    internal void ApplySelection(string animationId)
    {
        AnimationResolution resolution = _catalog.Resolve(animationId);
        if (resolution.UsedFallback)
        {
            throw new AnimationValidationException(
                resolution.Diagnostic ?? "選取的自訂動畫無法使用。");
        }

        try
        {
            ApplyResolved(resolution.Entry);
            DiagnosticChanged?.Invoke(null);
        }
        catch (Exception exception)
        {
            TryLog(() => _logger.LogError(
                exception,
                "Custom animation frame load failed. {Operation} {Subsystem} {AnimationId} {ErrorCategory} {FaultState} {HResult}",
                "LoadCustomAnimationFrames",
                "Animation",
                animationId,
                "FrameLoad",
                "Faulted",
                exception.HResult));
            DiagnosticChanged?.Invoke("自訂動畫 frame 無法載入，已保留原本動畫。");
            throw;
        }
    }

    private void ApplyResolved(AnimationCatalogEntry entry)
    {
        if (entry.IsBuiltIn)
        {
            _frameSource.SetBuiltIn(_themeCoordinator?.ResolvedTheme ?? ResolvedTheme.Light);
            _controller.ReplaceFrameSet(entry.FrameCount);
            return;
        }

        if (entry.PhysicalDirectory is null)
            throw new AnimationValidationException("自訂動畫資料夾不存在。");
        AnimationManifest manifest = _storage.ReadManifest(entry.PhysicalDirectory);
        IReadOnlyList<BitmapSource> frames = _storage.LoadAndValidateFrames(
            entry.PhysicalDirectory,
            manifest);
        _frameSource.SetCustom(entry.Id, frames);
        _controller.ReplaceFrameSet(frames.Count);
    }

    private void TryLog(Action action)
    {
        try { action(); } catch { }
    }
}
