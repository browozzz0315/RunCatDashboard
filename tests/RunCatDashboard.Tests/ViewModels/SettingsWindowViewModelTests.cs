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
                250, true),
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

    private sealed class FakeSettingsApplicationService : ISettingsApplicationService
    {
        public AppSettings Current { get; private set; } = AppSettings.Defaults;
        public RunAtLoginState RunAtLoginState { get; } = new(false, false, null);
        internal int ApplyCount { get; private set; }
        internal Exception? ApplyException { get; init; }
        internal (bool, OverlayInteractionMode, OverlayHotKeyGesture, int, bool) LastDraft { get; private set; }
        public Task<RunAtLoginState> ApplyDraftAsync(
            bool dashboardVisible,
            OverlayInteractionMode interactionMode,
            OverlayHotKeyGesture interactionHotKey,
            int samplingIntervalMilliseconds,
            bool runAtLoginRequested,
            CancellationToken cancellationToken = default)
        {
            ApplyCount++;
            if (ApplyException is not null)
            {
                return Task.FromException<RunAtLoginState>(ApplyException);
            }
            LastDraft = (dashboardVisible, interactionMode, interactionHotKey,
                samplingIntervalMilliseconds, runAtLoginRequested);
            Current = Current with
            {
                Window = Current.Window with { IsDashboardVisible = dashboardVisible },
                Overlay = new OverlaySettings(interactionMode, interactionHotKey),
                Metrics = new MetricsSettings(samplingIntervalMilliseconds),
                Startup = new StartupSettings(runAtLoginRequested)
            };
            return Task.FromResult(new RunAtLoginState(runAtLoginRequested, runAtLoginRequested, null));
        }
    }
}
