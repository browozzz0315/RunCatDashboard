using RunCatDashboard.App.Settings;
using RunCatDashboard.App.Startup;
using RunCatDashboard.App.Theming;
using RunCatDashboard.App.ViewModels;
using RunCatDashboard.App.Windowing;

namespace RunCatDashboard.Tests.ViewModels;

public sealed class SettingsWindowViewModelTests
{
    [Fact]
    public void Cancel_ClosesWithoutApplyingDraft()
    {
        var application = new FakeSettingsApplicationService();
        var viewModel = new SettingsWindowViewModel(application)
        {
            IsDashboardVisible = false,
            SamplingIntervalMilliseconds = 5000
        };
        int closes = 0;
        viewModel.CloseRequested += () => closes++;

        viewModel.CancelCommand.Execute(null);

        Assert.Equal(0, application.ApplyCount);
        Assert.Equal(1, closes);
    }

    [Fact]
    public async Task Save_AppliesRuntimeReconcilesStartupAndCloses()
    {
        var application = new FakeSettingsApplicationService();
        var viewModel = new SettingsWindowViewModel(application)
        {
            IsDashboardVisible = false,
            HotKeyControl = false,
            HotKeyWindows = true,
            HotKeyKey = OverlayHotKeyKey.F12,
            InteractionMode = OverlayInteractionMode.Interactive,
            SamplingIntervalMilliseconds = 250,
            RunAtLoginRequested = true
        };
        int closes = 0;
        viewModel.CloseRequested += () => closes++;

        viewModel.SaveCommand.Execute(null);
        await viewModel.SaveCommand.ExecutionTask!;

        Assert.Equal(1, application.ApplyCount);
        Assert.Equal(
            (false, OverlayInteractionMode.Interactive,
                new OverlayHotKeyGesture(false, true, true, true, OverlayHotKeyKey.F12),
                OverlayHotKeyGesture.DashboardVisibilityDefault,
                250, true, OverlaySizeMode.Standard,
                OverlayFieldSettings.ForMode(OverlaySizeMode.Standard),
                OverlayDisplayPolicy.HideOverFullscreenApps),
            application.LastDraft);
        Assert.True(viewModel.RunAtLoginApplied);
        Assert.Equal(1, closes);
    }

    [Fact]
    public void DisplayText_IsAlwaysDerivedFromDraftGesture()
    {
        var viewModel = new SettingsWindowViewModel(new FakeSettingsApplicationService());

        viewModel.HotKeyAlt = false;
        viewModel.HotKeyShift = false;
        viewModel.HotKeyWindows = true;
        viewModel.HotKeyKey = OverlayHotKeyKey.D7;

        Assert.Equal(viewModel.InteractionHotKey.DisplayText,
            viewModel.InteractionHotKeyDisplayText);
        Assert.Equal("Ctrl + Win + 7", viewModel.InteractionHotKeyDisplayText);
    }

    [Fact]
    public void VisibilityDisplayText_IsAlwaysDerivedFromDraftGesture()
    {
        var viewModel = new SettingsWindowViewModel(new FakeSettingsApplicationService())
        {
            VisibilityHotKeyAlt = false,
            VisibilityHotKeyShift = false,
            VisibilityHotKeyWindows = true,
            VisibilityHotKeyKey = OverlayHotKeyKey.F12
        };

        Assert.Equal(viewModel.VisibilityHotKey.DisplayText,
            viewModel.VisibilityHotKeyDisplayText);
        Assert.Equal("Ctrl + Win + F12", viewModel.VisibilityHotKeyDisplayText);
    }

    [Fact]
    public void KeyCaptureStateAndAdapterModel_AreReusableForBothHotKeys()
    {
        var viewModel = new SettingsWindowViewModel(new FakeSettingsApplicationService());

        viewModel.BeginHotKeyCapture();
        Assert.True(viewModel.IsHotKeyCaptureActive);
        Assert.False(viewModel.IsVisibilityHotKeyCaptureActive);
        viewModel.ApplyCapturedHotKeyKey(OverlayHotKeyKey.F8);

        viewModel.BeginVisibilityHotKeyCapture();
        Assert.False(viewModel.IsHotKeyCaptureActive);
        Assert.True(viewModel.IsVisibilityHotKeyCaptureActive);
        viewModel.ApplyCapturedVisibilityHotKeyKey(OverlayHotKeyKey.D9);

        Assert.Equal(OverlayHotKeyKey.F8, viewModel.InteractionHotKey.Key);
        Assert.Equal(OverlayHotKeyKey.D9, viewModel.VisibilityHotKey.Key);
        Assert.False(viewModel.IsHotKeyCaptureActive);
        Assert.False(viewModel.IsVisibilityHotKeyCaptureActive);
    }

    [Fact]
    public void DuplicateDraftGestures_ShowPurposeSpecificInlineError()
    {
        var viewModel = new SettingsWindowViewModel(new FakeSettingsApplicationService())
        {
            VisibilityHotKey = OverlayHotKeyGesture.Default
        };

        Assert.Equal(OverlayHotKeyGesture.DuplicateGestureMessage,
            viewModel.InteractionHotKeyError);
        Assert.Equal(OverlayHotKeyGesture.DuplicateGestureMessage,
            viewModel.VisibilityHotKeyError);
    }

    [Fact]
    public async Task CapturedKey_SaveAndReopen_PreservesKeyAndWarningDoesNotBlock()
    {
        var application = new FakeSettingsApplicationService();
        var viewModel = new SettingsWindowViewModel(application)
        {
            HotKeyAlt = false,
            HotKeyShift = false
        };
        viewModel.ApplyCapturedHotKeyKey(OverlayHotKeyKey.S);

        Assert.Equal("S", viewModel.HotKeyKeyDisplayText);
        Assert.Equal(
            OverlayHotKeyGesture.CommonApplicationGestureWarning,
            viewModel.HotKeyWarning);

        viewModel.SaveCommand.Execute(null);
        await viewModel.SaveCommand.ExecutionTask!;
        var reopened = new SettingsWindowViewModel(application);

        Assert.Equal(OverlayHotKeyKey.S, reopened.HotKeyKey);
        Assert.Equal("S", reopened.HotKeyKeyDisplayText);
    }

    [Fact]
    public void EndHotKeyCapture_PreservesCurrentKey()
    {
        var viewModel = new SettingsWindowViewModel(new FakeSettingsApplicationService());
        OverlayHotKeyKey original = viewModel.HotKeyKey;

        viewModel.BeginHotKeyCapture();
        Assert.True(viewModel.IsHotKeyCaptureActive);

        viewModel.EndHotKeyCapture();

        Assert.False(viewModel.IsHotKeyCaptureActive);
        Assert.Equal(original, viewModel.HotKeyKey);
    }

    [Fact]
    public async Task SaveFailure_ShowsFaultAndKeepsWindowOpen()
    {
        var application = new FakeSettingsApplicationService
        {
            ApplyException = new HotKeyConfigurationException("快捷鍵無法套用。")
        };
        var viewModel = new SettingsWindowViewModel(application)
        {
            IsDashboardVisible = false
        };
        int closes = 0;
        viewModel.CloseRequested += () => closes++;

        viewModel.SaveCommand.Execute(null);
        await viewModel.SaveCommand.ExecutionTask!;

        Assert.Equal("快捷鍵無法套用。", viewModel.ValidationError);
        Assert.Equal(0, closes);
    }

    [Fact]
    public async Task ArgumentFailure_DoesNotExposeInternalParameterName()
    {
        var application = new FakeSettingsApplicationService
        {
            ApplyException = new ArgumentException(
                "快捷鍵格式無效。",
                "interactionHotKey")
        };
        var viewModel = new SettingsWindowViewModel(application)
        {
            IsDashboardVisible = false
        };

        viewModel.SaveCommand.Execute(null);
        await viewModel.SaveCommand.ExecutionTask!;

        Assert.DoesNotContain("Parameter", viewModel.ValidationError);
        Assert.DoesNotContain("interactionHotKey", viewModel.ValidationError);
        Assert.Equal(
            "Overlay 模式快捷鍵設定無效，請選擇其他組合。",
            viewModel.ValidationError);
    }

    [Fact]
    public void SizeModeChange_AppliesDefaultsAndAllowsFieldOverride()
    {
        var viewModel = new SettingsWindowViewModel(new FakeSettingsApplicationService());

        viewModel.SizeMode = OverlaySizeMode.Expanded;
        Assert.Equal(
            OverlayFieldSettings.ForMode(OverlaySizeMode.Expanded),
            viewModel.Fields);

        viewModel.ShowRecentCpuHistory = false;
        viewModel.ShowCpu = false;

        Assert.False(viewModel.ShowRecentCpuHistory);
        Assert.False(viewModel.ShowCpu);
        Assert.True(viewModel.ShowMemory);
    }

    [Fact]
    public void AssigningSameSizeMode_DoesNotResetCurrentDraft()
    {
        var viewModel = new SettingsWindowViewModel(new FakeSettingsApplicationService())
        {
            SizeMode = OverlaySizeMode.Expanded
        };
        viewModel.ShowHotKeyHints = false;

        viewModel.SizeMode = OverlaySizeMode.Expanded;

        Assert.False(viewModel.ShowHotKeyHints);
    }

    [Fact]
    public void CatOnly_NormalizesDraftToNoFieldsAndDisablesSelection()
    {
        var viewModel = new SettingsWindowViewModel(new FakeSettingsApplicationService())
        {
            SizeMode = OverlaySizeMode.CatOnly
        };

        Assert.Equal(
            OverlayFieldSettings.ForMode(OverlaySizeMode.CatOnly),
            viewModel.Fields);
        Assert.False(viewModel.IsFieldSelectionEnabled);
    }

    [Fact]
    public async Task Save_NonCatModeWithoutCpuOrMemory_ShowsValidationAndStaysOpen()
    {
        var application = new FakeSettingsApplicationService();
        var viewModel = new SettingsWindowViewModel(application)
        {
            ShowCpu = false,
            ShowMemory = false
        };
        int closes = 0;
        viewModel.CloseRequested += () => closes++;

        viewModel.SaveCommand.Execute(null);
        await viewModel.SaveCommand.ExecutionTask!;

        Assert.Contains("至少需要顯示 CPU 或 Memory", viewModel.ValidationError);
        Assert.Equal(0, closes);
        Assert.Equal(0, application.ApplyCount);
    }

    [Fact]
    public async Task SaveAndReopen_PersistsOnlyCurrentFieldSet()
    {
        var application = new FakeSettingsApplicationService();
        var viewModel = new SettingsWindowViewModel(application)
        {
            SizeMode = OverlaySizeMode.Expanded
        };
        viewModel.ShowCpu = false;
        viewModel.ShowHotKeyHints = false;

        viewModel.SaveCommand.Execute(null);
        await viewModel.SaveCommand.ExecutionTask!;
        var reopened = new SettingsWindowViewModel(application);

        Assert.Equal(OverlaySizeMode.Expanded, reopened.SizeMode);
        Assert.False(reopened.ShowCpu);
        Assert.True(reopened.ShowMemory);
        Assert.False(reopened.ShowHotKeyHints);
    }

    [Fact]
    public void NewWindow_StartsCleanWithApplyDisabledAndSaveEnabled()
    {
        var viewModel = new SettingsWindowViewModel(new FakeSettingsApplicationService());

        Assert.False(viewModel.IsDirty);
        Assert.False(viewModel.ApplyCommand.CanExecute(null));
        Assert.True(viewModel.SaveCommand.CanExecute(null));
    }

    [Fact]
    public void ThemePreference_ParticipatesInStructuralDirtyState()
    {
        var viewModel = new SettingsWindowViewModel(new FakeSettingsApplicationService());

        Assert.Equal(ThemePreference.System, viewModel.ThemePreference);
        viewModel.ThemePreference = ThemePreference.Dark;
        Assert.True(viewModel.IsDirty);

        viewModel.ThemePreference = ThemePreference.System;
        Assert.False(viewModel.IsDirty);
    }

    [Fact]
    public async Task ApplyTheme_UpdatesBaselineWithoutClosing()
    {
        var application = new FakeSettingsApplicationService();
        var viewModel = new SettingsWindowViewModel(application)
        {
            ThemePreference = ThemePreference.Dark
        };
        int closes = 0;
        viewModel.CloseRequested += () => closes++;

        viewModel.ApplyCommand.Execute(null);
        await viewModel.ApplyCommand.ExecutionTask!;

        Assert.Equal(ThemePreference.Dark, application.LastThemePreference);
        Assert.Equal(ThemePreference.Dark, application.Current.Appearance.ThemePreference);
        Assert.False(viewModel.IsDirty);
        Assert.Equal(0, closes);
    }

    [Fact]
    public async Task ApplyThemeThenEditAndCancel_LeavesAppliedTheme()
    {
        var application = new FakeSettingsApplicationService();
        var viewModel = new SettingsWindowViewModel(application)
        {
            ThemePreference = ThemePreference.Dark
        };

        viewModel.ApplyCommand.Execute(null);
        await viewModel.ApplyCommand.ExecutionTask!;
        viewModel.ThemePreference = ThemePreference.Light;
        viewModel.CancelCommand.Execute(null);

        Assert.Equal(ThemePreference.Dark, application.Current.Appearance.ThemePreference);
    }

    [Fact]
    public void EditAndRevert_TracksStructuralDirtyState()
    {
        var viewModel = new SettingsWindowViewModel(new FakeSettingsApplicationService());

        viewModel.SamplingIntervalMilliseconds = 500;
        Assert.True(viewModel.IsDirty);
        Assert.True(viewModel.ApplyCommand.CanExecute(null));

        viewModel.SamplingIntervalMilliseconds = 1000;
        Assert.False(viewModel.IsDirty);
        Assert.False(viewModel.ApplyCommand.CanExecute(null));
    }

    [Theory]
    [InlineData("dashboard")]
    [InlineData("interaction-mode")]
    [InlineData("interaction-hotkey")]
    [InlineData("visibility-hotkey")]
    [InlineData("sampling")]
    [InlineData("startup")]
    [InlineData("presentation")]
    [InlineData("fullscreen-policy")]
    public void EveryEditableSettingsGroup_ParticipatesInDirtyState(string group)
    {
        var viewModel = new SettingsWindowViewModel(new FakeSettingsApplicationService());

        switch (group)
        {
            case "dashboard":
                viewModel.IsDashboardVisible = false;
                break;
            case "interaction-mode":
                viewModel.InteractionMode = OverlayInteractionMode.Interactive;
                break;
            case "interaction-hotkey":
                viewModel.HotKeyKey = OverlayHotKeyKey.F8;
                break;
            case "visibility-hotkey":
                viewModel.VisibilityHotKeyKey = OverlayHotKeyKey.F9;
                break;
            case "sampling":
                viewModel.SamplingIntervalMilliseconds = 500;
                break;
            case "startup":
                viewModel.RunAtLoginRequested = true;
                break;
            case "presentation":
                viewModel.ShowRecentCpuHistory = true;
                break;
            case "fullscreen-policy":
                viewModel.RequestedDisplayPolicy = OverlayDisplayPolicy.NeverTopmost;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(group));
        }

        Assert.True(viewModel.IsDirty);
    }

    [Fact]
    public void ModeDefaultReset_ParticipatesInDirtyComparison()
    {
        var viewModel = new SettingsWindowViewModel(new FakeSettingsApplicationService());

        viewModel.SizeMode = OverlaySizeMode.Expanded;
        Assert.True(viewModel.IsDirty);
        Assert.Equal(OverlayFieldSettings.ForMode(OverlaySizeMode.Expanded), viewModel.Fields);

        viewModel.SizeMode = OverlaySizeMode.Standard;
        Assert.False(viewModel.IsDirty);
        Assert.Equal(OverlayFieldSettings.ForMode(OverlaySizeMode.Standard), viewModel.Fields);
    }

    [Fact]
    public async Task ApplySuccess_UsesPipelineOnceKeepsOpenUpdatesBaselineAndClearsError()
    {
        var application = new FakeSettingsApplicationService();
        var viewModel = new SettingsWindowViewModel(application)
        {
            IsDashboardVisible = false,
            ValidationError = "舊錯誤"
        };
        int closes = 0;
        viewModel.CloseRequested += () => closes++;

        viewModel.ApplyCommand.Execute(null);
        await viewModel.ApplyCommand.ExecutionTask!;

        Assert.Equal(1, application.ApplyCount);
        Assert.Equal(0, closes);
        Assert.False(viewModel.IsDirty);
        Assert.False(viewModel.ApplyCommand.CanExecute(null));
        Assert.Null(viewModel.ValidationError);
        Assert.False(application.Current.Window.IsDashboardVisible);
    }

    [Fact]
    public async Task ApplyFailure_KeepsWindowDraftAndBaselineForRetry()
    {
        var application = new FakeSettingsApplicationService
        {
            ApplyException = new HotKeyConfigurationException("快捷鍵無法套用。")
        };
        var viewModel = new SettingsWindowViewModel(application)
        {
            SamplingIntervalMilliseconds = 500
        };
        int closes = 0;
        viewModel.CloseRequested += () => closes++;

        viewModel.ApplyCommand.Execute(null);
        await viewModel.ApplyCommand.ExecutionTask!;

        Assert.Equal(0, closes);
        Assert.True(viewModel.IsDirty);
        Assert.Equal(500, viewModel.SamplingIntervalMilliseconds);
        Assert.Equal(1000, application.Current.Metrics.SamplingIntervalMilliseconds);

        application.ApplyException = null;
        viewModel.ApplyCommand.Execute(null);
        await viewModel.ApplyCommand.ExecutionTask!;

        Assert.Equal(2, application.ApplyCount);
        Assert.False(viewModel.IsDirty);
        Assert.Equal(500, application.Current.Metrics.SamplingIntervalMilliseconds);
    }

    [Fact]
    public async Task RepeatedApply_AfterNewEditAppliesEachDraftExactlyOnce()
    {
        var application = new FakeSettingsApplicationService();
        var viewModel = new SettingsWindowViewModel(application)
        {
            SamplingIntervalMilliseconds = 500
        };

        viewModel.ApplyCommand.Execute(null);
        await viewModel.ApplyCommand.ExecutionTask!;
        viewModel.SamplingIntervalMilliseconds = 250;
        viewModel.ApplyCommand.Execute(null);
        await viewModel.ApplyCommand.ExecutionTask!;

        Assert.Equal(2, application.ApplyCount);
        Assert.False(viewModel.IsDirty);
        Assert.Equal(250, application.Current.Metrics.SamplingIntervalMilliseconds);
    }

    [Fact]
    public async Task CleanSave_ClosesWithoutCallingApplicationPipeline()
    {
        var application = new FakeSettingsApplicationService();
        var viewModel = new SettingsWindowViewModel(application);
        int closes = 0;
        viewModel.CloseRequested += () => closes++;

        viewModel.SaveCommand.Execute(null);
        await viewModel.SaveCommand.ExecutionTask!;

        Assert.Equal(0, application.ApplyCount);
        Assert.Equal(1, closes);
    }

    [Fact]
    public async Task DirtySave_UsesSamePipelineAndClosesOnlyAfterSuccess()
    {
        var application = new FakeSettingsApplicationService();
        var viewModel = new SettingsWindowViewModel(application)
        {
            RunAtLoginRequested = true
        };
        int closes = 0;
        viewModel.CloseRequested += () => closes++;

        viewModel.SaveCommand.Execute(null);
        await viewModel.SaveCommand.ExecutionTask!;

        Assert.Equal(1, application.ApplyCount);
        Assert.Equal(1, closes);
        Assert.False(viewModel.IsDirty);
    }

    [Fact]
    public async Task ApplyThenEditAndCancel_LeavesLatestAppliedBaselineInRuntimeAndPersistence()
    {
        var application = new FakeSettingsApplicationService();
        var viewModel = new SettingsWindowViewModel(application)
        {
            SamplingIntervalMilliseconds = 500
        };

        viewModel.ApplyCommand.Execute(null);
        await viewModel.ApplyCommand.ExecutionTask!;
        viewModel.SamplingIntervalMilliseconds = 250;
        viewModel.CancelCommand.Execute(null);

        Assert.Equal(1, application.ApplyCount);
        Assert.Equal(500, application.Current.Metrics.SamplingIntervalMilliseconds);
    }

    [Fact]
    public async Task ReopenAfterApply_UsesLatestAppliedStateAsCleanBaseline()
    {
        var application = new FakeSettingsApplicationService();
        var viewModel = new SettingsWindowViewModel(application)
        {
            RequestedDisplayPolicy = OverlayDisplayPolicy.AlwaysOnTop,
            SizeMode = OverlaySizeMode.Compact
        };

        viewModel.ApplyCommand.Execute(null);
        await viewModel.ApplyCommand.ExecutionTask!;
        var reopened = new SettingsWindowViewModel(application);

        Assert.False(reopened.IsDirty);
        Assert.Equal(OverlayDisplayPolicy.AlwaysOnTop, reopened.RequestedDisplayPolicy);
        Assert.Equal(OverlaySizeMode.Compact, reopened.SizeMode);
        Assert.Equal(OverlayFieldSettings.ForMode(OverlaySizeMode.Compact), reopened.Fields);
    }

    [Fact]
    public async Task Applying_DisablesAllActionsAndPreventsOverlappingPipelines()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var application = new FakeSettingsApplicationService { ApplyGate = gate };
        var viewModel = new SettingsWindowViewModel(application)
        {
            SamplingIntervalMilliseconds = 500
        };

        viewModel.ApplyCommand.Execute(null);
        Task firstApplication = viewModel.ApplyCommand.ExecutionTask!;
        Assert.Equal(1, application.ApplyCount);
        Assert.True(viewModel.IsApplying);
        Assert.False(viewModel.ApplyCommand.CanExecute(null));
        Assert.False(viewModel.SaveCommand.CanExecute(null));
        Assert.False(viewModel.CancelCommand.CanExecute(null));

        viewModel.ApplyCommand.Execute(null);
        viewModel.SaveCommand.Execute(null);
        Assert.Equal(1, application.ApplyCount);

        gate.SetResult();
        await firstApplication;

        Assert.False(viewModel.IsApplying);
        Assert.True(viewModel.SaveCommand.CanExecute(null));
        Assert.True(viewModel.CancelCommand.CanExecute(null));
    }

    private sealed class FakeSettingsApplicationService : ISettingsApplicationService
    {
        public AppSettings Current { get; private set; } = AppSettings.Defaults;
        public RunAtLoginState RunAtLoginState { get; } = new(false, false, null);
        public OverlayDisplayPolicy CurrentDisplayPolicy { get; private set; } =
            OverlayDisplayPolicy.HideOverFullscreenApps;
        internal int ApplyCount { get; private set; }
        internal Exception? ApplyException { get; set; }
        internal TaskCompletionSource? ApplyGate { get; init; }
        internal (bool, OverlayInteractionMode, OverlayHotKeyGesture,
            OverlayHotKeyGesture, int, bool, OverlaySizeMode,
            OverlayFieldSettings, OverlayDisplayPolicy) LastDraft { get; private set; }
        internal ThemePreference LastThemePreference { get; private set; }
        public async Task<RunAtLoginState> ApplyDraftAsync(
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
            ApplyCount++;
            if (ApplyGate is not null)
            {
                await ApplyGate.Task.WaitAsync(cancellationToken);
            }
            if (ApplyException is not null)
            {
                throw ApplyException;
            }
            fields ??= OverlayFieldSettings.ForMode(sizeMode);
            if (!AppSettingsValidator.TryValidatePresentation(sizeMode, fields, out string? error))
            {
                throw new ArgumentException(error, nameof(fields));
            }
            LastDraft = (dashboardVisible, interactionMode, interactionHotKey, visibilityHotKey,
                samplingIntervalMilliseconds, runAtLoginRequested, sizeMode, fields, displayPolicy);
            LastThemePreference = themePreference;
            CurrentDisplayPolicy = displayPolicy;
            Current = Current with
            {
                Window = Current.Window with
                {
                    IsDashboardVisible = dashboardVisible,
                    VisibilityHotKey = visibilityHotKey
                },
                Overlay = new OverlaySettings(interactionMode, interactionHotKey, sizeMode, fields),
                Metrics = new MetricsSettings(samplingIntervalMilliseconds),
                Startup = new StartupSettings(runAtLoginRequested),
                Appearance = new AppearanceSettings(themePreference)
            };
            return new RunAtLoginState(runAtLoginRequested, runAtLoginRequested, null);
        }
    }
}
