using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RunCatDashboard.App.Settings;
using RunCatDashboard.App.Windowing;

namespace RunCatDashboard.App.ViewModels;

public sealed partial class SettingsWindowViewModel : ObservableObject
{
    private readonly ISettingsApplicationService _applicationService;

    [ObservableProperty] private bool _isDashboardVisible;
    [ObservableProperty] private OverlayInteractionMode _interactionMode;
    [ObservableProperty] private OverlayHotKeyGesture _interactionHotKey;
    [ObservableProperty] private OverlayHotKeyGesture _visibilityHotKey;
    [ObservableProperty] private int _samplingIntervalMilliseconds;
    [ObservableProperty] private bool _runAtLoginRequested;
    [ObservableProperty] private bool _runAtLoginApplied;
    [ObservableProperty] private string? _startupFault;
    [ObservableProperty] private string? _validationError;
    [ObservableProperty] private string? _hotKeyCaptureMessage;
    [ObservableProperty] private bool _isHotKeyCaptureActive;
    [ObservableProperty] private string? _visibilityHotKeyCaptureMessage;
    [ObservableProperty] private bool _isVisibilityHotKeyCaptureActive;

    public SettingsWindowViewModel(ISettingsApplicationService applicationService)
    {
        ArgumentNullException.ThrowIfNull(applicationService);
        _applicationService = applicationService;
        AppSettings settings = applicationService.Current;
        _isDashboardVisible = settings.Window.IsDashboardVisible;
        _interactionMode = settings.Overlay.InteractionMode;
        _interactionHotKey = settings.Overlay.InteractionHotKey ??
            OverlayHotKeyGesture.Default;
        _visibilityHotKey = settings.Window.VisibilityHotKey ??
            OverlayHotKeyGesture.DashboardVisibilityDefault;
        _samplingIntervalMilliseconds = settings.Metrics.SamplingIntervalMilliseconds;
        _runAtLoginRequested = settings.Startup.RunAtLoginRequested;
        _runAtLoginApplied = applicationService.RunAtLoginState.Applied;
        _startupFault = applicationService.RunAtLoginState.Fault;
        SaveCommand = new AsyncRelayCommand(SaveAsync);
        CancelCommand = new RelayCommand(() =>
        {
            EndHotKeyCapture();
            CloseRequested?.Invoke();
        });
    }

    public IReadOnlyList<int> SamplingIntervals { get; } = [250, 500, 1000, 2000, 5000];
    public IReadOnlyList<OverlayInteractionMode> InteractionModes { get; } =
        [OverlayInteractionMode.Interactive, OverlayInteractionMode.ClickThrough];
    public bool HotKeyControl
    {
        get => InteractionHotKey.Control;
        set => InteractionHotKey = InteractionHotKey with { Control = value };
    }
    public bool HotKeyAlt
    {
        get => InteractionHotKey.Alt;
        set => InteractionHotKey = InteractionHotKey with { Alt = value };
    }
    public bool HotKeyShift
    {
        get => InteractionHotKey.Shift;
        set => InteractionHotKey = InteractionHotKey with { Shift = value };
    }
    public bool HotKeyWindows
    {
        get => InteractionHotKey.Windows;
        set => InteractionHotKey = InteractionHotKey with { Windows = value };
    }
    public OverlayHotKeyKey HotKeyKey
    {
        get => InteractionHotKey.Key;
        set => InteractionHotKey = InteractionHotKey with { Key = value };
    }
    public string InteractionHotKeyDisplayText => InteractionHotKey.DisplayText;
    public string HotKeyKeyDisplayText => OverlayHotKeyGesture.FormatKey(InteractionHotKey.Key);
    public string? HotKeyWarning => InteractionHotKey.UsageWarning;
    public string? InteractionHotKeyError => GetGestureError(
        InteractionHotKey,
        VisibilityHotKey,
        "Overlay 模式");
    public bool VisibilityHotKeyControl
    {
        get => VisibilityHotKey.Control;
        set => VisibilityHotKey = VisibilityHotKey with { Control = value };
    }
    public bool VisibilityHotKeyAlt
    {
        get => VisibilityHotKey.Alt;
        set => VisibilityHotKey = VisibilityHotKey with { Alt = value };
    }
    public bool VisibilityHotKeyShift
    {
        get => VisibilityHotKey.Shift;
        set => VisibilityHotKey = VisibilityHotKey with { Shift = value };
    }
    public bool VisibilityHotKeyWindows
    {
        get => VisibilityHotKey.Windows;
        set => VisibilityHotKey = VisibilityHotKey with { Windows = value };
    }
    public OverlayHotKeyKey VisibilityHotKeyKey
    {
        get => VisibilityHotKey.Key;
        set => VisibilityHotKey = VisibilityHotKey with { Key = value };
    }
    public string VisibilityHotKeyDisplayText => VisibilityHotKey.DisplayText;
    public string VisibilityHotKeyKeyDisplayText =>
        OverlayHotKeyGesture.FormatKey(VisibilityHotKey.Key);
    public string? VisibilityHotKeyWarning => VisibilityHotKey.UsageWarning;
    public string? VisibilityHotKeyError => GetGestureError(
        VisibilityHotKey,
        InteractionHotKey,
        "Dashboard 顯示／隱藏");
    public IAsyncRelayCommand SaveCommand { get; }
    public IRelayCommand CancelCommand { get; }
    public event Action? CloseRequested;

    public void BeginHotKeyCapture()
    {
        IsVisibilityHotKeyCaptureActive = false;
        IsHotKeyCaptureActive = true;
        HotKeyCaptureMessage = null;
    }

    public void BeginVisibilityHotKeyCapture()
    {
        IsHotKeyCaptureActive = false;
        IsVisibilityHotKeyCaptureActive = true;
        VisibilityHotKeyCaptureMessage = null;
    }

    public void EndHotKeyCapture()
    {
        IsHotKeyCaptureActive = false;
        IsVisibilityHotKeyCaptureActive = false;
    }

    public void ApplyCapturedVisibilityHotKeyKey(OverlayHotKeyKey key)
    {
        if (!OverlayHotKeyGesture.SupportedKeys.Contains(key))
        {
            throw new ArgumentOutOfRangeException(nameof(key));
        }

        VisibilityHotKeyKey = key;
        VisibilityHotKeyCaptureMessage = null;
        EndHotKeyCapture();
    }

    public void ApplyCapturedHotKeyKey(OverlayHotKeyKey key)
    {
        if (!OverlayHotKeyGesture.SupportedKeys.Contains(key))
        {
            throw new ArgumentOutOfRangeException(nameof(key));
        }

        HotKeyKey = key;
        HotKeyCaptureMessage = null;
        EndHotKeyCapture();
    }

    private async Task SaveAsync()
    {
        EndHotKeyCapture();
        ValidationError = null;
        try
        {
            var state = await _applicationService.ApplyDraftAsync(
                IsDashboardVisible,
                InteractionMode,
                InteractionHotKey,
                VisibilityHotKey,
                SamplingIntervalMilliseconds,
                RunAtLoginRequested);
            RunAtLoginApplied = state.Applied;
            StartupFault = state.Fault;
            CloseRequested?.Invoke();
        }
        catch (Exception exception) when (
            exception is ArgumentException or HotKeyConfigurationException)
        {
            ValidationError = GetUserFacingValidationMessage(exception);
        }
    }

    private static string? GetGestureError(
        OverlayHotKeyGesture gesture,
        OverlayHotKeyGesture otherGesture,
        string purpose)
    {
        if (gesture == otherGesture)
        {
            return OverlayHotKeyGesture.DuplicateGestureMessage;
        }

        return gesture.TryValidate(out string? error)
            ? null
            : error ?? $"{purpose}快捷鍵設定無效。";
    }

    private static string GetUserFacingValidationMessage(Exception exception)
    {
        if (exception is HotKeyConfigurationException)
        {
            return exception.Message;
        }

        if (exception is ArgumentException argumentException)
        {
            if (string.Equals(
                argumentException.ParamName,
                nameof(InteractionHotKey),
                StringComparison.OrdinalIgnoreCase))
            {
                return "Overlay 模式快捷鍵設定無效，請選擇其他組合。";
            }

            if (string.Equals(
                argumentException.ParamName,
                nameof(InteractionMode),
                StringComparison.OrdinalIgnoreCase))
            {
                return "請選擇有效的互動模式。";
            }

            if (string.Equals(
                argumentException.ParamName,
                nameof(SamplingIntervalMilliseconds),
                StringComparison.OrdinalIgnoreCase))
            {
                return "請選擇有效的 Metrics sampling interval。";
            }

            return "設定值無效，請檢查後再試。";
        }

        return "設定無法儲存，請檢查後再試。";
    }

    partial void OnInteractionHotKeyChanged(OverlayHotKeyGesture value)
    {
        ValidationError = null;
        OnPropertyChanged(nameof(HotKeyControl));
        OnPropertyChanged(nameof(HotKeyAlt));
        OnPropertyChanged(nameof(HotKeyShift));
        OnPropertyChanged(nameof(HotKeyWindows));
        OnPropertyChanged(nameof(HotKeyKey));
        OnPropertyChanged(nameof(InteractionHotKeyDisplayText));
        OnPropertyChanged(nameof(HotKeyKeyDisplayText));
        OnPropertyChanged(nameof(HotKeyWarning));
        OnPropertyChanged(nameof(InteractionHotKeyError));
        OnPropertyChanged(nameof(VisibilityHotKeyError));
    }

    partial void OnVisibilityHotKeyChanged(OverlayHotKeyGesture value)
    {
        ValidationError = null;
        OnPropertyChanged(nameof(VisibilityHotKeyControl));
        OnPropertyChanged(nameof(VisibilityHotKeyAlt));
        OnPropertyChanged(nameof(VisibilityHotKeyShift));
        OnPropertyChanged(nameof(VisibilityHotKeyWindows));
        OnPropertyChanged(nameof(VisibilityHotKeyKey));
        OnPropertyChanged(nameof(VisibilityHotKeyDisplayText));
        OnPropertyChanged(nameof(VisibilityHotKeyKeyDisplayText));
        OnPropertyChanged(nameof(VisibilityHotKeyWarning));
        OnPropertyChanged(nameof(VisibilityHotKeyError));
        OnPropertyChanged(nameof(InteractionHotKeyError));
    }
}
