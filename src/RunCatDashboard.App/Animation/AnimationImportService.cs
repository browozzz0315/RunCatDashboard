using System.Globalization;
using System.IO;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging;
using RunCatDashboard.App.Settings;

namespace RunCatDashboard.App.Animation;

internal sealed class AnimationImportService
{
    private readonly SpriteSheetParser _parser;
    private readonly AnimationLibraryStorage _storage;
    private readonly AnimationCatalog _catalog;
    private readonly ILogger<AnimationImportService> _logger;

    internal AnimationImportService(
        SpriteSheetParser parser,
        AnimationLibraryStorage storage,
        AnimationCatalog catalog,
        ILogger<AnimationImportService>? logger = null)
    {
        _parser = parser;
        _storage = storage;
        _catalog = catalog;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<AnimationImportService>.Instance;
    }

    internal AnimationImportPreview Preview(
        string sourcePath,
        int frameCount)
    {
        return _parser.Parse(sourcePath, frameCount);
    }

    internal AnimationCatalogEntry Import(
        string sourcePath,
        int frameCount,
        string displayName)
    {
        string normalizedName = displayName?.Trim() ?? string.Empty;
        if (normalizedName.Length == 0)
            throw new AnimationValidationException("顯示名稱不可為空白。");
        if (!_catalog.IsDisplayNameAvailable(normalizedName))
            throw new AnimationValidationException("自訂動畫顯示名稱已存在，請使用其他名稱。");

        AnimationImportPreview preview = _parser.Parse(sourcePath, frameCount);
        string animationId = $"custom-{Guid.NewGuid():N}";
        var manifest = new AnimationManifest(
            AnimationSettings.CurrentFormatVersion,
            animationId,
            normalizedName,
            "png",
            preview.FrameCount,
            preview.FrameWidth,
            preview.FrameHeight,
            250);

        string? temporaryDirectory = null;
        try
        {
            temporaryDirectory = _storage.CreateTemporaryDirectory();
            _storage.WriteAnimation(temporaryDirectory, manifest, preview.Frames);
            AnimationManifest reRead = _storage.ReadManifest(temporaryDirectory);
            _storage.LoadAndValidateFrames(temporaryDirectory, reRead);
            _storage.Publish(temporaryDirectory, animationId);
            temporaryDirectory = null;
            _catalog.Refresh();
            AnimationCatalogEntry? entry = _catalog.Find(animationId);
            if (entry is null || !entry.IsValid)
                throw new AnimationValidationException("匯入完成後重新驗證失敗。");

            TryLog(() => _logger.LogInformation(
                "Custom animation published. {Operation} {Subsystem} {AnimationId} {FrameCount} {FrameWidth} {FrameHeight}",
                "PublishCustomAnimation",
                "Animation",
                animationId,
                preview.FrameCount,
                preview.FrameWidth,
                preview.FrameHeight));
            return entry;
        }
        catch (AnimationValidationException)
        {
            CleanupTemporaryDirectory(temporaryDirectory);
            throw;
        }
        catch (Exception exception)
        {
            bool temporaryImportStillExists = temporaryDirectory is not null;
            CleanupTemporaryDirectory(temporaryDirectory);
            if (!temporaryImportStillExists)
            {
                throw;
            }

            TryLog(() => _logger.LogError(
                exception,
                "Custom animation publish failed. {Operation} {Subsystem} {AnimationId} {FaultState} {HResult}",
                "PublishCustomAnimation",
                "Animation",
                animationId,
                "Faulted",
                exception.HResult));
            throw new AnimationValidationException(
                "自訂動畫匯入失敗，未留下可用的部分資料。",
                exception);
        }
    }

    private void CleanupTemporaryDirectory(string? directory)
    {
        if (directory is null) return;
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            TryLog(() => _logger.LogWarning(
                exception,
                "Custom animation temporary cleanup failed. {Operation} {Subsystem} {FaultState}",
                "CleanupAnimationImport",
                "Animation",
                "Faulted"));
        }
    }

    private void TryLog(Action action)
    {
        try { action(); } catch { }
    }
}

internal sealed class AnimationFilePickerResult
{
    internal AnimationFilePickerResult(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Path = path;
    }

    internal string Path { get; }
}

internal interface IAnimationFilePicker
{
    AnimationFilePickerResult? PickPng();
}

internal sealed class WindowsAnimationFilePicker : IAnimationFilePicker
{
    public AnimationFilePickerResult? PickPng()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "PNG image (*.png)|*.png",
            CheckFileExists = true,
            Multiselect = false,
            Title = "選擇 RunCat sprite sheet"
        };
        return dialog.ShowDialog() == true
            ? new AnimationFilePickerResult(dialog.FileName)
            : null;
    }
}

internal interface IAnimationImportWindowService
{
    void Open(Action<AnimationCatalogEntry> imported);
}

internal interface IAnimationManagementService
{
    Task DeleteAsync(string animationId, CancellationToken cancellationToken = default);
}

internal sealed class AnimationManagementService : IAnimationManagementService
{
    private readonly ISettingsService _settings;
    private readonly RunCatAnimationRuntime _runtime;
    private readonly AnimationCatalog _catalog;
    private readonly ILogger<AnimationManagementService> _logger;

    internal AnimationManagementService(
        ISettingsService settings,
        RunCatAnimationRuntime runtime,
        AnimationCatalog catalog,
        ILogger<AnimationManagementService>? logger = null)
    {
        _settings = settings;
        _runtime = runtime;
        _catalog = catalog;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<AnimationManagementService>.Instance;
    }

    public async Task DeleteAsync(
        string animationId,
        CancellationToken cancellationToken = default)
    {
        AnimationCatalogEntry? entry = _catalog.Find(animationId);
        if (entry is null)
            throw new AnimationValidationException("找不到指定的自訂動畫。");
        if (entry.IsBuiltIn)
            throw new AnimationValidationException("內建動畫不可刪除。");

        string selectedId = _settings.Current.Animation.SelectedAnimationId ??
            AnimationSettings.BuiltInDefaultAnimationId;
        bool wasSelected = string.Equals(
            selectedId,
            animationId,
            StringComparison.Ordinal);
        if (wasSelected)
        {
            _runtime.ApplySelection(AnimationSettings.BuiltInDefaultAnimationId);
            bool persisted = await _settings.TryReplaceCurrentAsync(
                current => current with
                {
                    Animation = current.Animation with
                    {
                        SelectedAnimationId = AnimationSettings.BuiltInDefaultAnimationId,
                        FormatVersion = AnimationSettings.CurrentFormatVersion
                    }
                },
                cancellationToken).ConfigureAwait(false);
            if (!persisted)
            {
                try { _runtime.ApplySelection(animationId); }
                catch (Exception rollbackException)
                {
                    TryLog(() => _logger.LogError(
                        rollbackException,
                        "Selected animation delete rollback failed. {Operation} {Subsystem} {AnimationId} {FaultState}",
                        "DeleteSelectedAnimation",
                        "Animation",
                        animationId,
                        "Faulted"));
                }

                throw new AnimationValidationException("無法先保存內建動畫回退設定，未刪除自訂動畫。");
            }
        }

        try
        {
            _catalog.Delete(entry);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            TryLog(() => _logger.LogError(
                exception,
                "Custom animation deletion failed. {Operation} {Subsystem} {AnimationId} {FaultState} {HResult}",
                "DeleteCustomAnimation",
                "Animation",
                animationId,
                "Faulted",
                exception.HResult));
            throw new AnimationValidationException("刪除自訂動畫失敗，已保留動畫資料。", exception);
        }
    }

    private void TryLog(Action action)
    {
        try { action(); } catch { }
    }
}
