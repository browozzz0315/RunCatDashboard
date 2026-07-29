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
        OverlayHotKeyGesture interactionHotKey,
        int samplingIntervalMilliseconds,
        bool runAtLoginRequested,
        CancellationToken cancellationToken = default);
}

internal sealed class SettingsApplicationService : ISettingsApplicationService
{
    private readonly ISettingsService _settings;
    private readonly IWindowVisibilityCoordinator _visibility;
    private readonly IInteractionModeToggleAction _interaction;
    private readonly IGlobalHotKeyController _hotKeys;
    private readonly MainWindowViewModel _mainViewModel;
    private readonly IRunAtLoginService _runAtLogin;

    internal SettingsApplicationService(
        ISettingsService settings,
        IWindowVisibilityCoordinator visibility,
        IInteractionModeToggleAction interaction,
        IGlobalHotKeyController hotKeys,
        MainWindowViewModel mainViewModel,
        IRunAtLoginService runAtLogin)
    {
        _settings = settings;
        _visibility = visibility;
        _interaction = interaction;
        _hotKeys = hotKeys;
        _mainViewModel = mainViewModel;
        _runAtLogin = runAtLogin;
    }

    public AppSettings Current => _settings.Current;
    public RunAtLoginState RunAtLoginState => _runAtLogin.State;

    public async Task<RunAtLoginState> ApplyDraftAsync(
        bool dashboardVisible,
        OverlayInteractionMode interactionMode,
        OverlayHotKeyGesture interactionHotKey,
        int samplingIntervalMilliseconds,
        bool runAtLoginRequested,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(interactionMode))
            throw new ArgumentOutOfRangeException(nameof(interactionMode));
        if (!AppSettingsValidator.AllowedSamplingIntervals.Contains(samplingIntervalMilliseconds))
            throw new ArgumentOutOfRangeException(nameof(samplingIntervalMilliseconds));
        ArgumentNullException.ThrowIfNull(interactionHotKey);
        if (!interactionHotKey.TryValidate(out string? hotKeyValidationError))
            throw new HotKeyConfigurationException(
                hotKeyValidationError ?? "Overlay 模式快捷鍵設定無效。");

        GlobalHotKeyApplyResult hotKeyResult =
            _hotKeys.ApplyInteractionGesture(interactionHotKey);
        _mainViewModel.ApplyHotKeyRegistrations(_hotKeys.Registrations);
        if (!hotKeyResult.IsSuccess)
        {
            if (hotKeyResult.RequiresSafeRecovery)
            {
                _visibility.SetUserRequestedVisibility(true);
                _interaction.RequestMode(OverlayInteractionMode.Interactive);
            }

            throw new HotKeyConfigurationException(
                hotKeyResult.Fault ?? "Overlay 模式快捷鍵無法套用；設定未變更。");
        }

        _settings.Update(current => current with
        {
            Window = current.Window with { IsDashboardVisible = dashboardVisible },
            Overlay = new OverlaySettings(interactionMode, interactionHotKey),
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
