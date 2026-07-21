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
    [ObservableProperty] private int _samplingIntervalMilliseconds;
    [ObservableProperty] private bool _runAtLoginRequested;
    [ObservableProperty] private bool _runAtLoginApplied;
    [ObservableProperty] private string? _startupFault;
    [ObservableProperty] private string? _validationError;

    public SettingsWindowViewModel(ISettingsApplicationService applicationService)
    {
        ArgumentNullException.ThrowIfNull(applicationService);
        _applicationService = applicationService;
        AppSettings settings = applicationService.Current;
        _isDashboardVisible = settings.Window.IsDashboardVisible;
        _interactionMode = settings.Overlay.InteractionMode;
        _samplingIntervalMilliseconds = settings.Metrics.SamplingIntervalMilliseconds;
        _runAtLoginRequested = settings.Startup.RunAtLoginRequested;
        _runAtLoginApplied = applicationService.RunAtLoginState.Applied;
        _startupFault = applicationService.RunAtLoginState.Fault;
        SaveCommand = new AsyncRelayCommand(SaveAsync);
        CancelCommand = new RelayCommand(() => CloseRequested?.Invoke());
    }

    public IReadOnlyList<int> SamplingIntervals { get; } = [250, 500, 1000, 2000, 5000];
    public IReadOnlyList<OverlayInteractionMode> InteractionModes { get; } =
        [OverlayInteractionMode.Interactive, OverlayInteractionMode.ClickThrough];
    public IAsyncRelayCommand SaveCommand { get; }
    public IRelayCommand CancelCommand { get; }
    public event Action? CloseRequested;

    private async Task SaveAsync()
    {
        ValidationError = null;
        try
        {
            var state = await _applicationService.ApplyDraftAsync(
                IsDashboardVisible,
                InteractionMode,
                SamplingIntervalMilliseconds,
                RunAtLoginRequested);
            RunAtLoginApplied = state.Applied;
            StartupFault = state.Fault;
            CloseRequested?.Invoke();
        }
        catch (ArgumentException exception)
        {
            ValidationError = exception.Message;
        }
    }
}
