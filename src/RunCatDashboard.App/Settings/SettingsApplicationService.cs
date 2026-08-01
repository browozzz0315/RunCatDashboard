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
        OverlayHotKeyGesture visibilityHotKey,
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
        OverlayHotKeyGesture visibilityHotKey,
        int samplingIntervalMilliseconds,
        bool runAtLoginRequested,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(interactionMode))
            throw new ArgumentOutOfRangeException(nameof(interactionMode));
        if (!AppSettingsValidator.AllowedSamplingIntervals.Contains(samplingIntervalMilliseconds))
            throw new ArgumentOutOfRangeException(nameof(samplingIntervalMilliseconds));
        ArgumentNullException.ThrowIfNull(interactionHotKey);
        ArgumentNullException.ThrowIfNull(visibilityHotKey);
        if (!interactionHotKey.TryValidate(out string? hotKeyValidationError))
            throw new HotKeyConfigurationException(
                hotKeyValidationError ?? "Overlay 模式快捷鍵設定無效。");
        if (!visibilityHotKey.TryValidate(out string? visibilityHotKeyValidationError))
            throw new HotKeyConfigurationException(
                visibilityHotKeyValidationError ?? "Dashboard 顯示／隱藏快捷鍵設定無效。");
        if (interactionHotKey == visibilityHotKey)
            throw new HotKeyConfigurationException(OverlayHotKeyGesture.DuplicateGestureMessage);

        AppSettings previous = _settings.Current;
        OverlayHotKeyGesture previousInteraction =
            previous.Overlay.InteractionHotKey ?? OverlayHotKeyGesture.Default;
        OverlayHotKeyGesture previousVisibility =
            previous.Window.VisibilityHotKey ?? OverlayHotKeyGesture.DashboardVisibilityDefault;
        bool interactionChanged = interactionHotKey != previousInteraction;
        bool visibilityChanged = visibilityHotKey != previousVisibility;

        GlobalHotKeyApplyResult? interactionResult = null;
        if (interactionChanged)
        {
            interactionResult = _hotKeys.ApplyInteractionGesture(interactionHotKey);
            _mainViewModel.ApplyHotKeyRegistrations(_hotKeys.Registrations);
            if (!interactionResult.IsSuccess)
            {
                if (interactionResult.RequiresSafeRecovery)
                {
                    _visibility.SetUserRequestedVisibility(true);
                    _interaction.RequestMode(OverlayInteractionMode.Interactive);
                }
                if (!interactionResult.RollbackSucceeded)
                {
                    SynchronizeActualHotKeyGestures();
                }

                throw new HotKeyConfigurationException(
                    interactionResult.Fault ?? "Overlay 模式快捷鍵無法套用；設定未變更。");
            }
        }

        GlobalHotKeyApplyResult? visibilityResult = null;
        if (visibilityChanged)
        {
            visibilityResult = _hotKeys.ApplyVisibilityGesture(visibilityHotKey);
            _mainViewModel.ApplyHotKeyRegistrations(_hotKeys.Registrations);
            if (!visibilityResult.IsSuccess)
            {
                bool rollbackSucceeded = RollBackGesture(
                    interactionResult,
                    () => _hotKeys.ApplyInteractionGesture(previousInteraction),
                    isInteractionGesture: true);
                if (!visibilityResult.RollbackSucceeded || !rollbackSucceeded)
                {
                    SynchronizeActualHotKeyGestures();
                }
                if (visibilityResult.RequiresSafeRecovery)
                {
                    _visibility.SetUserRequestedVisibility(true);
                }

                throw new HotKeyConfigurationException(
                    visibilityResult.Fault ??
                    "Dashboard 顯示／隱藏快捷鍵無法套用；設定未變更。");
            }
        }

        bool saved;
        try
        {
            saved = await _settings
                .TryReplaceCurrentAsync(
                    latest => MergeDraftWithConcurrentUpdates(
                        previous,
                        latest,
                        dashboardVisible,
                        interactionMode,
                        interactionHotKey,
                        visibilityHotKey,
                        samplingIntervalMilliseconds,
                        runAtLoginRequested),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            bool visibilityRollbackSucceeded = RollBackGesture(
                visibilityResult,
                () => _hotKeys.ApplyVisibilityGesture(previousVisibility),
                isInteractionGesture: false);
            bool interactionRollbackSucceeded = RollBackGesture(
                interactionResult,
                () => _hotKeys.ApplyInteractionGesture(previousInteraction),
                isInteractionGesture: true);
            _mainViewModel.ApplyHotKeyRegistrations(_hotKeys.Registrations);
            if (!visibilityRollbackSucceeded || !interactionRollbackSucceeded)
            {
                SynchronizeActualHotKeyGestures();
            }
            throw;
        }
        if (!saved)
        {
            bool visibilityRollbackSucceeded = RollBackGesture(
                visibilityResult,
                () => _hotKeys.ApplyVisibilityGesture(previousVisibility),
                isInteractionGesture: false);
            bool interactionRollbackSucceeded = RollBackGesture(
                interactionResult,
                () => _hotKeys.ApplyInteractionGesture(previousInteraction),
                isInteractionGesture: true);
            _mainViewModel.ApplyHotKeyRegistrations(_hotKeys.Registrations);
            bool rollbackSucceeded =
                visibilityRollbackSucceeded && interactionRollbackSucceeded;
            if (!rollbackSucceeded)
            {
                SynchronizeActualHotKeyGestures();
            }
            throw new HotKeyConfigurationException(
                rollbackSucceeded
                    ? "設定無法保存，已恢復先前的快捷鍵與設定。"
                    : "設定無法保存，且部分快捷鍵未能恢復。請使用系統匣控制 Dashboard，並重新開啟設定確認目前狀態。");
        }

        AppSettings applied = _settings.Current;
        _visibility.SetUserRequestedVisibility(applied.Window.IsDashboardVisible);
        _interaction.RequestMode(applied.Overlay.InteractionMode);
        _mainViewModel.UpdateSamplingInterval(
            TimeSpan.FromMilliseconds(applied.Metrics.SamplingIntervalMilliseconds));
        RunAtLoginState state = await _runAtLogin
            .ReconcileAsync(applied.Startup.RunAtLoginRequested, cancellationToken)
            .ConfigureAwait(false);
        return state;
    }

    private bool RollBackGesture(
        GlobalHotKeyApplyResult? appliedResult,
        Func<GlobalHotKeyApplyResult> rollback,
        bool isInteractionGesture)
    {
        if (appliedResult is null || !appliedResult.IsSuccess || appliedResult.IsNoOp)
        {
            return true;
        }

        GlobalHotKeyApplyResult result = rollback();
        if (!result.IsSuccess && result.RequiresSafeRecovery)
        {
            _visibility.SetUserRequestedVisibility(true);
            if (isInteractionGesture)
            {
                _interaction.RequestMode(OverlayInteractionMode.Interactive);
            }
        }
        return result.IsSuccess;
    }

    private void SynchronizeActualHotKeyGestures()
    {
        _settings.Update(current => current with
        {
            Window = current.Window with
            {
                VisibilityHotKey = _hotKeys.VisibilityGesture
            },
            Overlay = current.Overlay with
            {
                InteractionHotKey = _hotKeys.InteractionGesture
            }
        });
        _mainViewModel.ApplyHotKeyRegistrations(_hotKeys.Registrations);
    }

    private static AppSettings MergeDraftWithConcurrentUpdates(
        AppSettings previous,
        AppSettings latest,
        bool dashboardVisible,
        OverlayInteractionMode interactionMode,
        OverlayHotKeyGesture interactionHotKey,
        OverlayHotKeyGesture visibilityHotKey,
        int samplingIntervalMilliseconds,
        bool runAtLoginRequested)
    {
        bool effectiveVisibility =
            latest.Window.IsDashboardVisible != previous.Window.IsDashboardVisible
                ? latest.Window.IsDashboardVisible
                : dashboardVisible;
        OverlayInteractionMode effectiveMode =
            latest.Overlay.InteractionMode != previous.Overlay.InteractionMode
                ? latest.Overlay.InteractionMode
                : interactionMode;
        MetricsSettings effectiveMetrics = latest.Metrics != previous.Metrics
            ? latest.Metrics
            : new MetricsSettings(samplingIntervalMilliseconds);
        StartupSettings effectiveStartup = latest.Startup != previous.Startup
            ? latest.Startup
            : new StartupSettings(runAtLoginRequested);

        return latest with
        {
            Window = latest.Window with
            {
                IsDashboardVisible = effectiveVisibility,
                VisibilityHotKey = visibilityHotKey
            },
            Overlay = latest.Overlay with
            {
                InteractionMode = effectiveMode,
                InteractionHotKey = interactionHotKey
            },
            Metrics = effectiveMetrics,
            Startup = effectiveStartup
        };
    }
}
