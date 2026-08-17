using RunCatDashboard.App.Startup;
using RunCatDashboard.App.Animation;
using RunCatDashboard.App.Services;
using RunCatDashboard.App.ViewModels;
using RunCatDashboard.App.Windowing;
using RunCatDashboard.App.Theming;
using System.Diagnostics;

namespace RunCatDashboard.App.Settings;

public interface ISettingsApplicationService
{
    AppSettings Current { get; }
    RunAtLoginState RunAtLoginState { get; }
    OverlayDisplayPolicy CurrentDisplayPolicy { get; }
    Task<RunAtLoginState> ApplyDraftAsync(
        bool dashboardVisible,
        OverlayInteractionMode interactionMode,
        OverlayHotKeyGesture interactionHotKey,
        OverlayHotKeyGesture visibilityHotKey,
        int samplingIntervalMilliseconds,
        bool runAtLoginRequested,
        OverlaySizeMode sizeMode = OverlaySizeMode.Standard,
        OverlayFieldSettings? fields = null,
        OverlayDisplayPolicy displayPolicy = OverlayDisplayPolicy.HideOverFullscreenApps,
        ThemePreference themePreference = ThemePreference.System,
        CancellationToken cancellationToken = default);

    Task<RunAtLoginState> ApplyDraftAsync(
        bool dashboardVisible,
        OverlayInteractionMode interactionMode,
        OverlayHotKeyGesture interactionHotKey,
        OverlayHotKeyGesture visibilityHotKey,
        int samplingIntervalMilliseconds,
        bool runAtLoginRequested,
        OverlaySizeMode sizeMode,
        OverlayFieldSettings? fields,
        OverlayDisplayPolicy displayPolicy,
        ThemePreference themePreference,
        string selectedAnimationId,
        AnimationSpeedPreference speedPreference,
        CancellationToken cancellationToken = default)
    {
        return ApplyDraftAsync(
            dashboardVisible,
            interactionMode,
            interactionHotKey,
            visibilityHotKey,
            samplingIntervalMilliseconds,
            runAtLoginRequested,
            sizeMode,
            fields,
            displayPolicy,
            themePreference,
            cancellationToken);
    }
}

internal sealed class SettingsApplicationService : ISettingsApplicationService
{
    private readonly ISettingsService _settings;
    private readonly IWindowVisibilityCoordinator _visibility;
    private readonly IInteractionModeToggleAction _interaction;
    private readonly IGlobalHotKeyController _hotKeys;
    private readonly MainWindowViewModel _mainViewModel;
    private readonly IRunAtLoginService _runAtLogin;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly IThemeCoordinator? _themeCoordinator;
    private readonly RunCatAnimationRuntime? _animationRuntime;

    internal SettingsApplicationService(
        ISettingsService settings,
        IWindowVisibilityCoordinator visibility,
        IInteractionModeToggleAction interaction,
        IGlobalHotKeyController hotKeys,
        MainWindowViewModel mainViewModel,
        IRunAtLoginService runAtLogin,
        IUiDispatcher uiDispatcher,
        IThemeCoordinator? themeCoordinator = null,
        RunCatAnimationRuntime? animationRuntime = null)
    {
        _settings = settings;
        _visibility = visibility;
        _interaction = interaction;
        _hotKeys = hotKeys;
        _mainViewModel = mainViewModel;
        _runAtLogin = runAtLogin;
        _uiDispatcher = uiDispatcher;
        _themeCoordinator = themeCoordinator;
        _animationRuntime = animationRuntime;
    }

    public AppSettings Current => _settings.Current;
    public RunAtLoginState RunAtLoginState => _runAtLogin.State;
    public OverlayDisplayPolicy CurrentDisplayPolicy =>
        _mainViewModel.RequestedDisplayPolicy;

    public Task<RunAtLoginState> ApplyDraftAsync(
        bool dashboardVisible,
        OverlayInteractionMode interactionMode,
        OverlayHotKeyGesture interactionHotKey,
        OverlayHotKeyGesture visibilityHotKey,
        int samplingIntervalMilliseconds,
        bool runAtLoginRequested,
        OverlaySizeMode sizeMode = OverlaySizeMode.Standard,
        OverlayFieldSettings? fields = null,
        OverlayDisplayPolicy displayPolicy = OverlayDisplayPolicy.HideOverFullscreenApps,
        ThemePreference themePreference = ThemePreference.System,
        CancellationToken cancellationToken = default)
    {
        AppSettings current = Current;
        return ApplyDraftAsync(
            dashboardVisible,
            interactionMode,
            interactionHotKey,
            visibilityHotKey,
            samplingIntervalMilliseconds,
            runAtLoginRequested,
            sizeMode,
            fields,
            displayPolicy,
            themePreference,
            current.Animation.SelectedAnimationId ?? AnimationSettings.BuiltInDefaultAnimationId,
            current.Animation.SpeedPreference,
            cancellationToken);
    }

    public async Task<RunAtLoginState> ApplyDraftAsync(
        bool dashboardVisible,
        OverlayInteractionMode interactionMode,
        OverlayHotKeyGesture interactionHotKey,
        OverlayHotKeyGesture visibilityHotKey,
        int samplingIntervalMilliseconds,
        bool runAtLoginRequested,
        OverlaySizeMode sizeMode,
        OverlayFieldSettings? fields,
        OverlayDisplayPolicy displayPolicy,
        ThemePreference themePreference,
        string selectedAnimationId,
        AnimationSpeedPreference speedPreference,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(interactionMode))
            throw new ArgumentOutOfRangeException(nameof(interactionMode));
        if (!AppSettingsValidator.AllowedSamplingIntervals.Contains(samplingIntervalMilliseconds))
            throw new ArgumentOutOfRangeException(nameof(samplingIntervalMilliseconds));
        fields ??= OverlayFieldSettings.ForMode(sizeMode);
        if (!AppSettingsValidator.TryValidatePresentation(sizeMode, fields, out string? presentationError))
            throw new ArgumentException(presentationError, nameof(fields));
        if (!Enum.IsDefined(displayPolicy))
            throw new ArgumentOutOfRangeException(nameof(displayPolicy));
        if (!Enum.IsDefined(themePreference))
            throw new ArgumentOutOfRangeException(nameof(themePreference));
        if (string.IsNullOrWhiteSpace(selectedAnimationId))
            selectedAnimationId = AnimationSettings.BuiltInDefaultAnimationId;
        selectedAnimationId = selectedAnimationId.Trim();
        if (!Enum.IsDefined(speedPreference))
            throw new ArgumentOutOfRangeException(nameof(speedPreference));
        if (_animationRuntime is not null &&
            _animationRuntime.Catalog.Find(selectedAnimationId) is not { IsValid: true })
        {
            throw new ArgumentException("選取的動畫不存在或已損壞。", nameof(selectedAnimationId));
        }
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
        bool dashboardVisibilityChanged =
            dashboardVisible != previous.Window.IsDashboardVisible;
        bool interactionModeChanged =
            interactionMode != previous.Overlay.InteractionMode;
        bool samplingIntervalChanged =
            samplingIntervalMilliseconds != previous.Metrics.SamplingIntervalMilliseconds;
        bool runAtLoginChanged =
            runAtLoginRequested != previous.Startup.RunAtLoginRequested;
        bool presentationChanged =
            sizeMode != previous.Overlay.SizeMode ||
            fields != (previous.Overlay.Fields ??
                OverlayFieldSettings.ForMode(previous.Overlay.SizeMode));
        bool themeChanged = themePreference != previous.Appearance.ThemePreference;
        string previousAnimationId = previous.Animation.SelectedAnimationId ??
            AnimationSettings.BuiltInDefaultAnimationId;
        AnimationSpeedPreference previousSpeed = previous.Animation.SpeedPreference;
        bool animationSelectionChanged = selectedAnimationId != previousAnimationId;
        bool animationSpeedChanged = speedPreference != previousSpeed;
        bool animationChanged = animationSelectionChanged || animationSpeedChanged;
        AppSettings? mergeBase = null;
        AppSettings? mergedCandidate = null;

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

        bool animationRuntimeChanged = false;
        bool saved;
        try
        {
            if (animationSelectionChanged && _animationRuntime is not null)
            {
                _animationRuntime.ApplySelection(selectedAnimationId);
                animationRuntimeChanged = true;
            }
            saved = await _settings
                .TryReplaceCurrentAsync(
                    latest =>
                    {
                        AppSettings candidate = MergeDraftWithConcurrentUpdates(
                            latest,
                            previous,
                            dashboardVisible,
                            interactionMode,
                            interactionHotKey,
                            visibilityHotKey,
                            samplingIntervalMilliseconds,
                            runAtLoginRequested,
                            sizeMode,
                            fields,
                            themePreference,
                            themeChanged,
                            selectedAnimationId,
                            speedPreference,
                            latestSnapshot => mergeBase = latestSnapshot);
                        mergedCandidate = candidate;
                        return candidate;
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            if (animationRuntimeChanged && _animationRuntime is not null)
            {
                try { _animationRuntime.ApplySelection(previousAnimationId); }
                catch (Exception rollbackException)
                {
                    RecordRollbackFailure("runtime-animation", rollbackException);
                }
            }
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
            if (animationRuntimeChanged && _animationRuntime is not null)
            {
                try { _animationRuntime.ApplySelection(previousAnimationId); }
                catch (Exception rollbackException)
                {
                    RecordRollbackFailure("runtime-animation", rollbackException);
                }
            }
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
        if (_themeCoordinator is not null)
        {
            try
            {
                await _themeCoordinator
                    .ApplyPreferenceAsync(applied.Appearance.ThemePreference, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch
            {
                (bool themeRuntimeRollbackSucceeded, Exception? themeRollbackFailure) =
                    await TryRestoreThemePreferenceAsync(
                        previous.Appearance.ThemePreference)
                        .ConfigureAwait(false);
                bool themePersistenceRollbackSucceeded = false;
                try
                {
                    themePersistenceRollbackSucceeded = await _settings
                        .TryReplaceCurrentAsync(
                            latest => RestoreAfterThemeFailure(
                                previous,
                                mergedCandidate ?? applied,
                                mergeBase ?? previous,
                                latest,
                                dashboardVisibilityChanged,
                                interactionModeChanged,
                                samplingIntervalChanged,
                                runAtLoginChanged,
                                presentationChanged,
                                interactionChanged,
                                visibilityChanged,
                                themeChanged,
                                animationChanged),
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception rollbackException)
                {
                    RecordRollbackFailure("persisted-settings", rollbackException);
                }
                if (!themePersistenceRollbackSucceeded)
                {
                    RecordRollbackFailure(
                        "persisted-settings",
                        new InvalidOperationException(
                            "Settings persistence rollback returned false."));
                }

                bool visibilityRollbackSucceeded = RollBackGesture(
                    visibilityResult,
                    () => _hotKeys.ApplyVisibilityGesture(previousVisibility),
                    isInteractionGesture: false);
                bool interactionRollbackSucceeded = RollBackGesture(
                    interactionResult,
                    () => _hotKeys.ApplyInteractionGesture(previousInteraction),
                    isInteractionGesture: true);
                _mainViewModel.ApplyHotKeyRegistrations(_hotKeys.Registrations);

                if (!themePersistenceRollbackSucceeded ||
                    !themeRuntimeRollbackSucceeded ||
                    !visibilityRollbackSucceeded ||
                    !interactionRollbackSucceeded)
                {
                    SynchronizeActualHotKeyGestures();
                }

                if (themeRollbackFailure is not null)
                {
                    RecordRollbackFailure("theme", themeRollbackFailure);
                }

                if (animationRuntimeChanged && _animationRuntime is not null)
                {
                    try { _animationRuntime.ApplySelection(previousAnimationId); }
                    catch (Exception rollbackException)
                    {
                        RecordRollbackFailure("runtime-animation", rollbackException);
                    }
                }

                throw;
            }
        }

        await _uiDispatcher.InvokeAsync(() =>
        {
            _mainViewModel.ApplyOverlayPresentation(
                applied.Overlay.SizeMode,
                applied.Overlay.Fields ?? OverlayFieldSettings.ForMode(applied.Overlay.SizeMode));
            _mainViewModel.RequestedDisplayPolicy = displayPolicy;
        });
        _visibility.SetUserRequestedVisibility(applied.Window.IsDashboardVisible);
        _interaction.RequestMode(applied.Overlay.InteractionMode);
        _mainViewModel.UpdateSamplingInterval(
            TimeSpan.FromMilliseconds(applied.Metrics.SamplingIntervalMilliseconds));
        double baseInterval = _animationRuntime?.Catalog
            .Find(applied.Animation.SelectedAnimationId)?.BaseFrameIntervalMilliseconds ?? 250d;
        _mainViewModel.ApplyAnimationTiming(baseInterval, applied.Animation.SpeedPreference);
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
        AppSettings latest,
        AppSettings previous,
        bool dashboardVisible,
        OverlayInteractionMode interactionMode,
        OverlayHotKeyGesture interactionHotKey,
        OverlayHotKeyGesture visibilityHotKey,
        int samplingIntervalMilliseconds,
        bool runAtLoginRequested,
        OverlaySizeMode sizeMode,
        OverlayFieldSettings fields,
        ThemePreference themePreference,
        bool themeChanged,
        string selectedAnimationId,
        AnimationSpeedPreference speedPreference,
        Action<AppSettings>? observeLatest = null)
    {
        observeLatest?.Invoke(latest);
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
        bool presentationChangedConcurrently =
            latest.Overlay.SizeMode != previous.Overlay.SizeMode ||
            latest.Overlay.Fields != previous.Overlay.Fields;
        OverlaySizeMode effectiveSizeMode = presentationChangedConcurrently
            ? latest.Overlay.SizeMode
            : sizeMode;
        OverlayFieldSettings effectiveFields = presentationChangedConcurrently
            ? latest.Overlay.Fields ?? OverlayFieldSettings.ForMode(latest.Overlay.SizeMode)
            : fields;
        ThemePreference effectiveTheme = themeChanged
            ? themePreference
            : latest.Appearance.ThemePreference;
        AnimationSettings effectiveAnimation =
            latest.Animation != previous.Animation
                ? latest.Animation
                : new AnimationSettings(
                    selectedAnimationId,
                    speedPreference,
                    AnimationSettings.CurrentFormatVersion);

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
                InteractionHotKey = interactionHotKey,
                SizeMode = effectiveSizeMode,
                Fields = effectiveFields
            },
            Metrics = effectiveMetrics,
            Startup = effectiveStartup,
            Appearance = new AppearanceSettings(effectiveTheme),
            Animation = effectiveAnimation
        };
    }

    private static AppSettings RestoreAfterThemeFailure(
        AppSettings previous,
        AppSettings applied,
        AppSettings mergeBase,
        AppSettings latest,
        bool dashboardVisibilityChanged,
        bool interactionModeChanged,
        bool samplingIntervalChanged,
        bool runAtLoginChanged,
        bool presentationChanged,
        bool interactionHotKeyChanged,
        bool visibilityHotKeyChanged,
        bool themeChanged,
        bool animationChanged)
    {
        bool dashboardVisible = RestoreValue(
            previous.Window.IsDashboardVisible,
            applied.Window.IsDashboardVisible,
            mergeBase.Window.IsDashboardVisible,
            latest.Window.IsDashboardVisible,
            dashboardVisibilityChanged);
        OverlayInteractionMode interactionMode = RestoreValue(
            previous.Overlay.InteractionMode,
            applied.Overlay.InteractionMode,
            mergeBase.Overlay.InteractionMode,
            latest.Overlay.InteractionMode,
            interactionModeChanged);
        MetricsSettings metrics = RestoreValue(
            previous.Metrics,
            applied.Metrics,
            mergeBase.Metrics,
            latest.Metrics,
            samplingIntervalChanged);
        StartupSettings startup = RestoreValue(
            previous.Startup,
            applied.Startup,
            mergeBase.Startup,
            latest.Startup,
            runAtLoginChanged);
        OverlaySizeMode sizeMode = RestoreValue(
            previous.Overlay.SizeMode,
            applied.Overlay.SizeMode,
            mergeBase.Overlay.SizeMode,
            latest.Overlay.SizeMode,
            presentationChanged);
        OverlayFieldSettings fields = RestoreValue(
            previous.Overlay.Fields ?? OverlayFieldSettings.ForMode(previous.Overlay.SizeMode),
            applied.Overlay.Fields ?? OverlayFieldSettings.ForMode(applied.Overlay.SizeMode),
            mergeBase.Overlay.Fields ?? OverlayFieldSettings.ForMode(mergeBase.Overlay.SizeMode),
            latest.Overlay.Fields ?? OverlayFieldSettings.ForMode(latest.Overlay.SizeMode),
            presentationChanged);
        OverlayHotKeyGesture interactionHotKey = RestoreValue(
            previous.Overlay.InteractionHotKey ?? OverlayHotKeyGesture.Default,
            applied.Overlay.InteractionHotKey ?? OverlayHotKeyGesture.Default,
            mergeBase.Overlay.InteractionHotKey ?? OverlayHotKeyGesture.Default,
            latest.Overlay.InteractionHotKey ?? OverlayHotKeyGesture.Default,
            interactionHotKeyChanged);
        OverlayHotKeyGesture visibilityHotKey = RestoreValue(
            previous.Window.VisibilityHotKey ?? OverlayHotKeyGesture.DashboardVisibilityDefault,
            applied.Window.VisibilityHotKey ?? OverlayHotKeyGesture.DashboardVisibilityDefault,
            mergeBase.Window.VisibilityHotKey ?? OverlayHotKeyGesture.DashboardVisibilityDefault,
            latest.Window.VisibilityHotKey ?? OverlayHotKeyGesture.DashboardVisibilityDefault,
            visibilityHotKeyChanged);
        ThemePreference theme = RestoreValue(
            previous.Appearance.ThemePreference,
            applied.Appearance.ThemePreference,
            mergeBase.Appearance.ThemePreference,
            latest.Appearance.ThemePreference,
            themeChanged);
        AnimationSettings animation = RestoreValue(
            previous.Animation,
            applied.Animation,
            mergeBase.Animation,
            latest.Animation,
            animationChanged);

        return latest with
        {
            Window = latest.Window with
            {
                IsDashboardVisible = dashboardVisible,
                VisibilityHotKey = visibilityHotKey
            },
            Overlay = latest.Overlay with
            {
                InteractionMode = interactionMode,
                InteractionHotKey = interactionHotKey,
                SizeMode = sizeMode,
                Fields = fields
            },
            Metrics = metrics,
            Startup = startup,
            Appearance = new AppearanceSettings(theme),
            Animation = animation
        };
    }

    private static T RestoreValue<T>(
        T previous,
        T applied,
        T mergeBase,
        T latest,
        bool draftChanged)
        where T : notnull
    {
        if (!draftChanged || !EqualityComparer<T>.Default.Equals(latest, applied))
        {
            return latest;
        }

        return EqualityComparer<T>.Default.Equals(mergeBase, previous)
            ? previous
            : mergeBase;
    }

    private async Task<(bool Succeeded, Exception? Failure)> TryRestoreThemePreferenceAsync(
        ThemePreference preference)
    {
        if (_themeCoordinator is null)
        {
            return (true, null);
        }

        try
        {
            await _themeCoordinator
                .ApplyPreferenceAsync(preference)
                .ConfigureAwait(false);
            return (true, null);
        }
        catch (Exception exception)
        {
            RecordRollbackFailure("runtime-theme", exception);
            return (false, exception);
        }
    }

    private static void RecordRollbackFailure(string operation, Exception exception)
    {
        Trace.TraceError(
            "Settings Apply rollback failed for {0}: {1}",
            operation,
            exception);
    }

}
