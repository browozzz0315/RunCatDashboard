using System.IO;
using RunCatDashboard.App.Animation;
using RunCatDashboard.App.Models;
using RunCatDashboard.App.Services;
using RunCatDashboard.App.Settings;
using RunCatDashboard.App.Startup;
using RunCatDashboard.App.ViewModels;
using RunCatDashboard.App.Windowing;

namespace RunCatDashboard.Tests.Settings;

public sealed class SettingsApplicationServiceTests
{
    [Fact]
    public async Task ApplyDraft_NewViewModelUsesSavedInteractionHotKey()
    {
        var settings = new FakeSettingsService([]);
        var hotKeys = new FakeHotKeyController([]);
        await using MainWindowViewModel mainViewModel = CreateMainViewModel();
        var service = new SettingsApplicationService(
            settings,
            new WindowVisibilityCoordinator(),
            new FakeInteractionAction(),
            hotKeys,
            mainViewModel,
            new FakeRunAtLoginService());
        var gesture = new OverlayHotKeyGesture(
            true, false, true, true, OverlayHotKeyKey.F9);

        var firstViewModel = new SettingsWindowViewModel(service)
        {
            HotKeyAlt = false,
            HotKeyWindows = true
        };
        firstViewModel.ApplyCapturedHotKeyKey(gesture.Key);
        firstViewModel.SaveCommand.Execute(null);
        await firstViewModel.SaveCommand.ExecutionTask!;
        var reopenedViewModel = new SettingsWindowViewModel(service);

        Assert.Equal(gesture, reopenedViewModel.InteractionHotKey);
        Assert.Equal(gesture.DisplayText, reopenedViewModel.InteractionHotKeyDisplayText);
    }

    [Fact]
    public async Task ApplyDraft_PersistsInteractionHotKeyThatCanBeReloaded()
    {
        string directory = Path.Combine(
            Path.GetTempPath(), "RunCatDashboard.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new JsonSettingsStore(directory, new PhysicalSettingsFileSystem());
            await using var settings = new SettingsService(store);
            await settings.LoadAsync();
            var hotKeys = new FakeHotKeyController([]);
            await using MainWindowViewModel mainViewModel = CreateMainViewModel();
            var service = new SettingsApplicationService(
                settings,
                new WindowVisibilityCoordinator(),
                new FakeInteractionAction(),
                hotKeys,
                mainViewModel,
                new FakeRunAtLoginService());
            var gesture = new OverlayHotKeyGesture(
                false, true, true, true, OverlayHotKeyKey.F11);

            var viewModel = new SettingsWindowViewModel(service)
            {
                IsDashboardVisible = false,
                InteractionMode = OverlayInteractionMode.Interactive,
                HotKeyControl = false,
                HotKeyWindows = true,
                SamplingIntervalMilliseconds = 500,
                RunAtLoginRequested = true
            };
            viewModel.ApplyCapturedHotKeyKey(gesture.Key);
            viewModel.SaveCommand.Execute(null);
            await viewModel.SaveCommand.ExecutionTask!;
            SettingsLoadResult reloaded = await store.LoadAsync();

            Assert.Equal(AppSettings.CurrentVersion, reloaded.Settings.Version);
            Assert.Equal(gesture, reloaded.Settings.Overlay.InteractionHotKey);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ApplyDraft_HotKeySucceeds_PersistsOnlyAfterRuntimeApply()
    {
        var operations = new List<string>();
        var settings = new FakeSettingsService(operations);
        var hotKeys = new FakeHotKeyController(operations)
        {
            Result = new GlobalHotKeyApplyResult(true, false, true, false, null)
        };
        var visibility = new WindowVisibilityCoordinator();
        var interaction = new FakeInteractionAction();
        await using MainWindowViewModel mainViewModel = CreateMainViewModel();
        var service = new SettingsApplicationService(
            settings,
            visibility,
            interaction,
            hotKeys,
            mainViewModel,
            new FakeRunAtLoginService());
        var gesture = new OverlayHotKeyGesture(
            true, false, true, false, OverlayHotKeyKey.F9);

        await service.ApplyDraftAsync(
            false,
            OverlayInteractionMode.Interactive,
            gesture,
            500,
            true);

        Assert.Equal(["apply-hotkey", "update-settings", "flush-settings"], operations);
        Assert.Equal(gesture, settings.Current.Overlay.InteractionHotKey);
        Assert.False(settings.Current.Window.IsDashboardVisible);
        Assert.Equal(OverlayInteractionMode.Interactive,
            settings.Current.Overlay.InteractionMode);
        Assert.Equal(500, settings.Current.Metrics.SamplingIntervalMilliseconds);
        Assert.True(settings.Current.Startup.RunAtLoginRequested);

    }

    [Fact]
    public async Task ApplyDraft_HotKeyFails_DoesNotOverwriteOldSettings()
    {
        var operations = new List<string>();
        var settings = new FakeSettingsService(operations);
        AppSettings original = settings.Current;
        var hotKeys = new FakeHotKeyController(operations)
        {
            Result = new GlobalHotKeyApplyResult(
                false, false, true, false, "新快捷鍵失敗，已 rollback。")
        };
        var visibility = new WindowVisibilityCoordinator();
        var interaction = new FakeInteractionAction();
        await using MainWindowViewModel mainViewModel = CreateMainViewModel();
        var service = new SettingsApplicationService(
            settings,
            visibility,
            interaction,
            hotKeys,
            mainViewModel,
            new FakeRunAtLoginService());

        HotKeyConfigurationException exception = await Assert.ThrowsAsync<HotKeyConfigurationException>(
            () => service.ApplyDraftAsync(
                false,
                OverlayInteractionMode.Interactive,
                new OverlayHotKeyGesture(true, false, false, false, OverlayHotKeyKey.F8),
                250,
                true));

        Assert.Contains("rollback", exception.Message);
        Assert.Equal(["apply-hotkey"], operations);
        Assert.Equal(original, settings.Current);
        Assert.Empty(settings.PersistedSettings);
    }

    [Fact]
    public async Task ApplyDraft_DashboardVisibilityGesture_DoesNotCallHotKeyController()
    {
        var operations = new List<string>();
        var settings = new FakeSettingsService(operations);
        var hotKeys = new FakeHotKeyController(operations);
        await using MainWindowViewModel mainViewModel = CreateMainViewModel();
        var service = new SettingsApplicationService(
            settings,
            new WindowVisibilityCoordinator(),
            new FakeInteractionAction(),
            hotKeys,
            mainViewModel,
            new FakeRunAtLoginService());
        var gesture = new OverlayHotKeyGesture(
            true, true, true, false, OverlayHotKeyKey.D);

        HotKeyConfigurationException exception =
            await Assert.ThrowsAsync<HotKeyConfigurationException>(() =>
                service.ApplyDraftAsync(
                    true,
                    OverlayInteractionMode.ClickThrough,
                    gesture,
                    1000,
                    false));

        Assert.Equal(OverlayHotKeyGesture.DashboardVisibilityConflictMessage, exception.Message);
        Assert.Empty(operations);
        Assert.Equal(AppSettings.Defaults, settings.Current);
    }

    [Fact]
    public async Task Save_DashboardVisibilityGesture_ShowsSpecificUiErrorAndStaysOpen()
    {
        var operations = new List<string>();
        var settings = new FakeSettingsService(operations);
        await using MainWindowViewModel mainViewModel = CreateMainViewModel();
        var service = new SettingsApplicationService(
            settings,
            new WindowVisibilityCoordinator(),
            new FakeInteractionAction(),
            new FakeHotKeyController(operations),
            mainViewModel,
            new FakeRunAtLoginService());
        var viewModel = new SettingsWindowViewModel(service)
        {
            HotKeyKey = OverlayHotKeyKey.D
        };
        int closes = 0;
        viewModel.CloseRequested += () => closes++;

        viewModel.SaveCommand.Execute(null);
        await viewModel.SaveCommand.ExecutionTask!;

        Assert.Equal(
            OverlayHotKeyGesture.DashboardVisibilityConflictMessage,
            viewModel.ValidationError);
        Assert.Equal(0, closes);
        Assert.Empty(operations);
        Assert.DoesNotContain("Parameter", viewModel.ValidationError);
        Assert.DoesNotContain("interactionHotKey", viewModel.ValidationError);
    }

    [Fact]
    public async Task Save_NoModifier_ShowsErrorThenCorrectedGestureClearsErrorAndCloses()
    {
        var operations = new List<string>();
        var settings = new FakeSettingsService(operations);
        await using MainWindowViewModel mainViewModel = CreateMainViewModel();
        var service = new SettingsApplicationService(
            settings,
            new WindowVisibilityCoordinator(),
            new FakeInteractionAction(),
            new FakeHotKeyController(operations),
            mainViewModel,
            new FakeRunAtLoginService());
        var viewModel = new SettingsWindowViewModel(service)
        {
            HotKeyControl = false,
            HotKeyAlt = false,
            HotKeyShift = false,
            HotKeyWindows = false
        };
        int closes = 0;
        viewModel.CloseRequested += () => closes++;

        Assert.False(viewModel.HotKeyControl);
        Assert.False(viewModel.HotKeyAlt);
        Assert.False(viewModel.HotKeyShift);
        Assert.False(viewModel.HotKeyWindows);

        viewModel.SaveCommand.Execute(null);
        await viewModel.SaveCommand.ExecutionTask!;

        Assert.Equal("Overlay 模式快捷鍵至少需要一個 modifier。", viewModel.ValidationError);
        Assert.Equal(0, closes);
        Assert.Empty(operations);

        viewModel.HotKeyControl = true;

        Assert.Null(viewModel.ValidationError);
        viewModel.SaveCommand.Execute(null);
        await viewModel.SaveCommand.ExecutionTask!;

        Assert.Null(viewModel.ValidationError);
        Assert.Equal(1, closes);
        Assert.Equal(["apply-hotkey", "update-settings", "flush-settings"], operations);
    }

    [Fact]
    public async Task ApplyDraft_HotKeyAndRollbackFail_EntersVisibleInteractiveSafeState()
    {
        var operations = new List<string>();
        var settings = new FakeSettingsService(operations);
        var hotKeys = new FakeHotKeyController(operations)
        {
            Result = new GlobalHotKeyApplyResult(
                false, false, false, true, "新舊快捷鍵都失敗。")
        };
        var visibility = new WindowVisibilityCoordinator();
        visibility.SetUserRequestedVisibility(false);
        var interaction = new FakeInteractionAction();
        await using MainWindowViewModel mainViewModel = CreateMainViewModel();
        var service = new SettingsApplicationService(
            settings,
            visibility,
            interaction,
            hotKeys,
            mainViewModel,
            new FakeRunAtLoginService());

        await Assert.ThrowsAsync<HotKeyConfigurationException>(() =>
            service.ApplyDraftAsync(
                false,
                OverlayInteractionMode.ClickThrough,
                new OverlayHotKeyGesture(true, false, false, false, OverlayHotKeyKey.F8),
                1000,
                false));

        Assert.True(visibility.State.IsUserRequestedVisible);
        Assert.Equal(OverlayInteractionMode.Interactive, interaction.LastRequestedMode);
        Assert.Equal(AppSettings.Defaults, settings.Current);
    }

    private static MainWindowViewModel CreateMainViewModel() => new(
        new NoOpMetricsService(),
        new ImmediateDispatcher(),
        new NoOpAnimationController());

    private sealed class FakeSettingsService(List<string> operations) : ISettingsService
    {
        public AppSettings Current { get; private set; } = AppSettings.Defaults;
        internal List<AppSettings> PersistedSettings { get; } = [];
        public string? LastDiagnostic => null;
        public event Action<AppSettings>? Changed;
        public event Action<string?>? DiagnosticChanged { add { } remove { } }
        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public bool Update(Func<AppSettings, AppSettings> update)
        {
            operations.Add("update-settings");
            Current = AppSettingsValidator.Normalize(update(Current));
            Changed?.Invoke(Current);
            return true;
        }
        public Task FlushAsync(CancellationToken cancellationToken = default)
        {
            operations.Add("flush-settings");
            PersistedSettings.Add(Current);
            return Task.CompletedTask;
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeHotKeyController(List<string> operations) : IGlobalHotKeyController
    {
        public GlobalHotKeyApplyResult Result { get; init; } =
            new(true, false, true, false, null);
        public OverlayHotKeyGesture InteractionGesture { get; private set; } =
            OverlayHotKeyGesture.Default;
        public IReadOnlyList<GlobalHotKeyRegistrationState> Registrations =>
        [
            new(
                GlobalHotKeyAction.ToggleInteractionMode,
                GlobalHotKeyController.InteractionHotKeyIdentifier,
                InteractionGesture.DisplayText,
                Result.IsSuccess,
                Result.Fault,
                null)
        ];
        public IReadOnlyList<GlobalHotKeyRegistrationState> RegisterAll(nint windowHandle) =>
            Registrations;
        public GlobalHotKeyApplyResult ApplyInteractionGesture(OverlayHotKeyGesture gesture)
        {
            operations.Add("apply-hotkey");
            if (Result.IsSuccess)
            {
                InteractionGesture = gesture;
            }
            return Result;
        }
        public bool TryGetAction(int message, nint parameter, out GlobalHotKeyAction action)
        {
            action = default;
            return false;
        }
        public void Dispose() { }
    }

    private sealed class FakeInteractionAction : IInteractionModeToggleAction
    {
        public OverlayInteractionMode? LastRequestedMode { get; private set; }
        public OverlayWindowState State { get; private set; } = new(
            OverlayInteractionMode.ClickThrough,
            OverlayInteractionMode.ClickThrough,
            true,
            false,
            null);
        public event Action<OverlayWindowState>? StateChanged { add { } remove { } }
        public void RequestToggle() { }
        public void RequestMode(OverlayInteractionMode mode)
        {
            LastRequestedMode = mode;
            State = State with { RequestedMode = mode, AppliedMode = mode };
        }
    }

    private sealed class FakeRunAtLoginService : IRunAtLoginService
    {
        public RunAtLoginState State { get; private set; } = new(false, false, null);
        public Task<RunAtLoginState> ReconcileAsync(
            bool requested,
            CancellationToken cancellationToken = default)
        {
            State = new(requested, requested, null);
            return Task.FromResult(State);
        }
    }

    private sealed class NoOpMetricsService : ISystemMetricsService
    {
        public ValueTask<SystemMetricsSnapshot> SampleAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<SystemMetricsSnapshot>(
                new InvalidOperationException("Sampling is not used by this test."));
    }

    private sealed class ImmediateDispatcher : IUiDispatcher
    {
        public ValueTask InvokeAsync(Action action, CancellationToken cancellationToken = default)
        {
            action();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class NoOpAnimationController : IRunCatAnimationController
    {
        public int FrameCount => 1;
        public int FrameIndex => 0;
        public TimeSpan Interval { get; private set; }
        public bool IsRunning => false;
        public string? LastFault => null;
        public event Action<int>? FrameChanged { add { } remove { } }
        public event Action<string>? Faulted { add { } remove { } }
        public bool Start() => false;
        public void Stop() { }
        public bool UpdateInterval(TimeSpan interval)
        {
            Interval = interval;
            return true;
        }
        public void Dispose() { }
    }
}
