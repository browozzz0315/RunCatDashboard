using RunCatDashboard.App.Settings;
using RunCatDashboard.App.Startup;
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
        var viewModel = new SettingsWindowViewModel(application);
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
        var viewModel = new SettingsWindowViewModel(application);

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

    private sealed class FakeSettingsApplicationService : ISettingsApplicationService
    {
        public AppSettings Current { get; private set; } = AppSettings.Defaults;
        public RunAtLoginState RunAtLoginState { get; } = new(false, false, null);
        public OverlayDisplayPolicy CurrentDisplayPolicy { get; private set; } =
            OverlayDisplayPolicy.HideOverFullscreenApps;
        internal int ApplyCount { get; private set; }
        internal Exception? ApplyException { get; init; }
        internal (bool, OverlayInteractionMode, OverlayHotKeyGesture,
            OverlayHotKeyGesture, int, bool, OverlaySizeMode,
            OverlayFieldSettings, OverlayDisplayPolicy) LastDraft { get; private set; }
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
            CancellationToken cancellationToken = default)
        {
            ApplyCount++;
            if (ApplyException is not null)
            {
                return Task.FromException<RunAtLoginState>(ApplyException);
            }
            fields ??= OverlayFieldSettings.ForMode(sizeMode);
            if (!AppSettingsValidator.TryValidatePresentation(sizeMode, fields, out string? error))
            {
                return Task.FromException<RunAtLoginState>(
                    new ArgumentException(error, nameof(fields)));
            }
            LastDraft = (dashboardVisible, interactionMode, interactionHotKey, visibilityHotKey,
                samplingIntervalMilliseconds, runAtLoginRequested, sizeMode, fields, displayPolicy);
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
                Startup = new StartupSettings(runAtLoginRequested)
            };
            return Task.FromResult(new RunAtLoginState(runAtLoginRequested, runAtLoginRequested, null));
        }
    }
}
