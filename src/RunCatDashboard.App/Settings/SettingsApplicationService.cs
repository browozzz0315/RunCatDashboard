using RunCatDashboard.App.Startup;
using RunCatDashboard.App.ViewModels;
using RunCatDashboard.App.Windowing;

namespace RunCatDashboard.App.Settings;

public interface ISettingsApplicationService
{
    AppSettings Current { get; }
    RunAtLoginState RunAtLoginState { get; }
    Task<RunAtLoginState> ApplyDraftAsync(
        bool dashboardVisible,
        OverlayInteractionMode interactionMode,
        int samplingIntervalMilliseconds,
        bool runAtLoginRequested,
        CancellationToken cancellationToken = default);
}

internal sealed class SettingsApplicationService : ISettingsApplicationService
{
    private readonly ISettingsService _settings;
    private readonly IWindowVisibilityCoordinator _visibility;
    private readonly IInteractionModeToggleAction _interaction;
    private readonly MainWindowViewModel _mainViewModel;
    private readonly IRunAtLoginService _runAtLogin;

    internal SettingsApplicationService(
        ISettingsService settings,
        IWindowVisibilityCoordinator visibility,
        IInteractionModeToggleAction interaction,
        MainWindowViewModel mainViewModel,
        IRunAtLoginService runAtLogin)
    {
        _settings = settings;
        _visibility = visibility;
        _interaction = interaction;
        _mainViewModel = mainViewModel;
        _runAtLogin = runAtLogin;
    }

    public AppSettings Current => _settings.Current;
    public RunAtLoginState RunAtLoginState => _runAtLogin.State;

    public async Task<RunAtLoginState> ApplyDraftAsync(
        bool dashboardVisible,
        OverlayInteractionMode interactionMode,
        int samplingIntervalMilliseconds,
        bool runAtLoginRequested,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(interactionMode))
            throw new ArgumentOutOfRangeException(nameof(interactionMode));
        if (!AppSettingsValidator.AllowedSamplingIntervals.Contains(samplingIntervalMilliseconds))
            throw new ArgumentOutOfRangeException(nameof(samplingIntervalMilliseconds));

        _settings.Update(current => current with
        {
            Window = current.Window with { IsDashboardVisible = dashboardVisible },
            Overlay = new OverlaySettings(interactionMode),
            Metrics = new MetricsSettings(samplingIntervalMilliseconds),
            Startup = new StartupSettings(runAtLoginRequested)
        });
        _visibility.SetUserRequestedVisibility(dashboardVisible);
        _interaction.RequestMode(interactionMode);
        _mainViewModel.UpdateSamplingInterval(
            TimeSpan.FromMilliseconds(samplingIntervalMilliseconds));
        RunAtLoginState state = await _runAtLogin
            .ReconcileAsync(runAtLoginRequested, cancellationToken)
            .ConfigureAwait(false);
        await _settings.FlushAsync(cancellationToken).ConfigureAwait(false);
        return state;
    }
}
