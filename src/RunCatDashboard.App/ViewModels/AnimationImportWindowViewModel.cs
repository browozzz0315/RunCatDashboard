using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RunCatDashboard.App.Animation;

namespace RunCatDashboard.App.ViewModels;

public sealed partial class AnimationImportWindowViewModel : ObservableObject
{
    private readonly IAnimationFilePicker _filePicker;
    private readonly AnimationImportService _importService;
    private AnimationImportPreview? _preview;

    [ObservableProperty] private string? _sourcePath;
    [ObservableProperty] private int _frameCount = 8;
    [ObservableProperty] private string _displayName = string.Empty;
    [ObservableProperty] private int? _sourceWidth;
    [ObservableProperty] private int? _sourceHeight;
    [ObservableProperty] private int? _frameWidth;
    [ObservableProperty] private int? _frameHeight;
    [ObservableProperty] private IReadOnlyList<object> _previewFrames =
        Array.Empty<object>();
    [ObservableProperty] private int _previewFrameIndex;
    [ObservableProperty] private string? _validationError;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private bool _isImporting;

    internal AnimationImportWindowViewModel(
        IAnimationFilePicker filePicker,
        AnimationImportService importService)
    {
        _filePicker = filePicker;
        _importService = importService;
        ChooseSourceCommand = new RelayCommand(ChooseSource);
        ConfirmImportCommand = new RelayCommand(ConfirmImport, CanConfirmImport);
        CancelCommand = new RelayCommand(() => CloseRequested?.Invoke());
    }

    public IRelayCommand ChooseSourceCommand { get; }
    public IRelayCommand ConfirmImportCommand { get; }
    public IRelayCommand CancelCommand { get; }
    public object? PreviewFrame =>
        PreviewFrameIndex >= 0 && PreviewFrameIndex < PreviewFrames.Count
            ? PreviewFrames[PreviewFrameIndex]
            : null;

    public event Action? CloseRequested;
    public event Action<AnimationCatalogEntry>? Imported;

    internal void AdvancePreviewFrame()
    {
        if (PreviewFrames.Count <= 1)
        {
            return;
        }

        PreviewFrameIndex = (PreviewFrameIndex + 1) % PreviewFrames.Count;
    }

    private void ChooseSource()
    {
        AnimationFilePickerResult? result = _filePicker.PickPng();
        if (result is null)
        {
            return;
        }

        SourcePath = result.Path;
        RefreshPreview();
    }

    private void RefreshPreview()
    {
        ValidationError = null;
        StatusMessage = null;
        _preview = null;
        PreviewFrames = Array.Empty<object>();
        PreviewFrameIndex = 0;
        OnPropertyChanged(nameof(PreviewFrame));
        if (string.IsNullOrWhiteSpace(SourcePath))
        {
            ConfirmImportCommand.NotifyCanExecuteChanged();
            return;
        }

        try
        {
            _preview = _importService.Preview(SourcePath, FrameCount);
            SourceWidth = _preview.SourceWidth;
            SourceHeight = _preview.SourceHeight;
            FrameWidth = _preview.FrameWidth;
            FrameHeight = _preview.FrameHeight;
            PreviewFrames = _preview.Frames.Cast<object>().ToArray();
            StatusMessage = "PNG 已解碼，請確認顯示名稱後匯入。";
        }
        catch (AnimationValidationException exception)
        {
            SourceWidth = null;
            SourceHeight = null;
            FrameWidth = null;
            FrameHeight = null;
            ValidationError = exception.Message;
        }

        OnPropertyChanged(nameof(PreviewFrame));
        ConfirmImportCommand.NotifyCanExecuteChanged();
    }

    private bool CanConfirmImport() =>
        !IsImporting && _preview is not null && !string.IsNullOrWhiteSpace(DisplayName);

    private void ConfirmImport()
    {
        if (!CanConfirmImport() || SourcePath is null)
        {
            return;
        }

        IsImporting = true;
        ValidationError = null;
        try
        {
            AnimationCatalogEntry entry = _importService.Import(
                SourcePath,
                FrameCount,
                DisplayName);
            Imported?.Invoke(entry);
            CloseRequested?.Invoke();
        }
        catch (AnimationValidationException exception)
        {
            ValidationError = exception.Message;
        }
        finally
        {
            IsImporting = false;
            ConfirmImportCommand.NotifyCanExecuteChanged();
        }
    }

    partial void OnFrameCountChanged(int value)
    {
        RefreshPreview();
    }

    partial void OnPreviewFrameIndexChanged(int value) =>
        OnPropertyChanged(nameof(PreviewFrame));

    partial void OnPreviewFramesChanged(IReadOnlyList<object> value) =>
        OnPropertyChanged(nameof(PreviewFrame));

    partial void OnDisplayNameChanged(string value) =>
        ConfirmImportCommand.NotifyCanExecuteChanged();

    partial void OnIsImportingChanged(bool value) =>
        ConfirmImportCommand.NotifyCanExecuteChanged();
}
