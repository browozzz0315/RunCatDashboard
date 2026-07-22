using RunCatDashboard.App.Interop;
using RunCatDashboard.App.Animation;
using RunCatDashboard.App.Windowing;

namespace RunCatDashboard.Tests.Windowing;

public sealed class SystemTrayServiceTests
{
    [Fact]
    public void MenuText_ReflectsNextVisibilityAndInteractionActions()
    {
        var fixture = new TrayFixture();
        fixture.Service.Initialize();

        Assert.Equal("隱藏 Dashboard", fixture.Adapter.VisibilityText);
        Assert.Equal("切換為 Interactive", fixture.Adapter.InteractionText);
        Assert.Equal(
            "停用系統匣動畫（改用靜態圖示）",
            fixture.Adapter.AnimationText);

        fixture.Adapter.FireVisibilityToggle();
        fixture.Adapter.FireInteractionToggle();

        Assert.Equal("顯示 Dashboard", fixture.Adapter.VisibilityText);
        Assert.Equal("切換為 Click-through", fixture.Adapter.InteractionText);
        fixture.Adapter.FireAnimationToggle();
        Assert.Equal("啟用系統匣動畫", fixture.Adapter.AnimationText);
        Assert.Equal(1, fixture.InteractionAction.RequestCount);
    }

    [Fact]
    public void TrayAndRHotKey_DispatchToSameInteractionToggleAction()
    {
        var fixture = new TrayFixture();
        fixture.Service.Initialize();
        var hotKeys = new GlobalHotKeyController(new SuccessfulNativeHotKeyApi());
        hotKeys.RegisterAll(new nint(1234));
        var handler = new OverlayHotKeyMessageHandler(
            hotKeys,
            fixture.InteractionAction,
            fixture.Visibility);

        fixture.Adapter.FireInteractionToggle();
        handler.TryHandleMessage(
            GlobalHotKeyController.WindowMessageHotKey,
            new nint(GlobalHotKeyController.InteractionHotKeyIdentifier));

        Assert.Equal(2, fixture.InteractionAction.RequestCount);
    }

    [Fact]
    public void InteractionToggleFailure_PreservesAppliedStateAndRetryMenuText()
    {
        var fixture = new TrayFixture();
        fixture.InteractionAction.FailToggle = true;
        fixture.Service.Initialize();

        fixture.Adapter.FireInteractionToggle();

        Assert.Equal(1, fixture.InteractionAction.RequestCount);
        Assert.Equal(
            OverlayInteractionMode.Interactive,
            fixture.InteractionAction.State.RequestedMode);
        Assert.Equal(
            OverlayInteractionMode.ClickThrough,
            fixture.InteractionAction.State.AppliedMode);
        Assert.Equal("configured mode failure", fixture.InteractionAction.State.LastError);
        Assert.Equal("切換為 Interactive", fixture.Adapter.InteractionText);
    }

    [Fact]
    public void LeftDoubleClick_TogglesVisibilityWhileSingleClickHasNoHandler()
    {
        var fixture = new TrayFixture();
        fixture.Service.Initialize();

        fixture.Adapter.FireDoubleClick();

        Assert.False(fixture.Visibility.State.IsUserRequestedVisible);
        Assert.Equal(1, fixture.Adapter.DoubleClickSubscriberCount);
    }

    [Fact]
    public void ExitMenu_RequestsTrueExitOnce()
    {
        var fixture = new TrayFixture();
        fixture.Service.Initialize();
        int exits = 0;
        fixture.Exit.ExitRequested += () => exits++;

        fixture.Adapter.FireExit();
        fixture.Adapter.FireExit();

        Assert.True(fixture.Exit.IsExitRequested);
        Assert.Equal(1, exits);
    }

    [Fact]
    public void SettingsMenu_PublishesSettingsRequested()
    {
        var fixture = new TrayFixture();
        fixture.Service.Initialize();
        int requests = 0;
        fixture.Service.SettingsRequested += () => requests++;

        fixture.Adapter.FireSettings();

        Assert.Equal(1, requests);
    }

    [Fact]
    public void TaskbarCreated_WhenRepeated_RecoversSameAdapterIdempotently()
    {
        var fixture = new TrayFixture();
        Assert.True(fixture.Service.Initialize());
        Assert.False(fixture.Service.Initialize());

        Assert.True(fixture.Service.TryHandleWindowMessage(fixture.MessageApi.Message));
        Assert.True(fixture.Service.TryHandleWindowMessage(fixture.MessageApi.Message));

        Assert.Equal(1, fixture.Adapter.ShowCount);
        Assert.Equal(2, fixture.Adapter.RecoveryCount);
        Assert.Equal(1, fixture.MessageApi.RegisterCount);
    }

    [Fact]
    public void TaskbarCreated_RestoresAnimatedOrStaticModeWithoutReinitializing()
    {
        var fixture = new TrayFixture();
        fixture.Service.Initialize();
        fixture.AnimationController.FireFrame(3);

        fixture.Service.TryHandleWindowMessage(fixture.MessageApi.Message);

        Assert.Equal(3, fixture.Adapter.CurrentAnimatedFrame);
        Assert.Equal(1, fixture.AnimationCoordinator.InitializeCount);

        fixture.Adapter.FireAnimationToggle();
        fixture.Service.TryHandleWindowMessage(fixture.MessageApi.Message);

        Assert.True(fixture.Adapter.IsStatic);
        Assert.Equal(1, fixture.AnimationCoordinator.InitializeCount);
    }

    [Fact]
    public void RecoveryFailure_IsRetainedAsDiagnostic()
    {
        var fixture = new TrayFixture();
        fixture.Service.Initialize();
        fixture.Adapter.RecoveryException = new InvalidOperationException("shell unavailable");

        fixture.Service.TryHandleWindowMessage(fixture.MessageApi.Message);

        Assert.Contains("恢復系統匣圖示失敗", fixture.Service.LastError);
        Assert.Contains("shell unavailable", fixture.Service.LastError);
    }

    [Fact]
    public void Initialize_WhenIconCannotBeShown_RetainsDiagnostic()
    {
        var fixture = new TrayFixture();
        fixture.Adapter.ShowException = new InvalidOperationException(
            "載入 RunCatDashboard 系統匣圖示失敗");

        Assert.False(fixture.Service.Initialize());

        Assert.Contains("系統匣初始化失敗", fixture.Service.LastError);
        Assert.Contains("載入 RunCatDashboard 系統匣圖示失敗", fixture.Service.LastError);
    }

    [Fact]
    public void Dispose_WhenRepeated_HidesAndDisposesOnce()
    {
        var fixture = new TrayFixture();
        fixture.Service.Initialize();

        fixture.Service.Dispose();
        fixture.Service.Dispose();

        Assert.Equal(1, fixture.Adapter.DisposeCount);
    }

    private sealed class TrayFixture
    {
        internal FakeTrayIconAdapter Adapter { get; } = new();
        internal FakeMessageApi MessageApi { get; } = new();
        internal WindowVisibilityCoordinator Visibility { get; } = new();
        internal FakeInteractionModeToggleAction InteractionAction { get; } = new();
        internal ApplicationExitCoordinator Exit { get; } = new();
        internal FakeRunCatAnimationController AnimationController { get; } = new();
        internal CountingTrayAnimationCoordinator AnimationCoordinator { get; }
        internal SystemTrayService Service { get; }

        internal TrayFixture()
        {
            AnimationCoordinator = new CountingTrayAnimationCoordinator(
                Adapter,
                AnimationController);
            Service = new SystemTrayService(
                Adapter,
                MessageApi,
                Visibility,
                InteractionAction,
                Exit,
                AnimationCoordinator);
        }
    }

    private sealed class CountingTrayAnimationCoordinator(
        ITrayIconAdapter adapter,
        IRunCatAnimationController animationController)
        : ITrayAnimationCoordinator
    {
        private readonly TrayAnimationCoordinator _inner =
            new(adapter, animationController);

        internal int InitializeCount { get; private set; }
        public bool IsAnimated => _inner.IsAnimated;
        public string? LastError => _inner.LastError;
        public event Action<string?>? DiagnosticChanged
        {
            add => _inner.DiagnosticChanged += value;
            remove => _inner.DiagnosticChanged -= value;
        }
        public bool Initialize()
        {
            InitializeCount++;
            return _inner.Initialize();
        }
        public bool ToggleMode() => _inner.ToggleMode();
        public void RestoreCurrentModeIcon() => _inner.RestoreCurrentModeIcon();
        public void Dispose() => _inner.Dispose();
    }

    private sealed class FakeTrayIconAdapter : ITrayIconAdapter
    {
        public event Action? DoubleClicked;
        public event Action? VisibilityToggleRequested;
        public event Action? InteractionToggleRequested;
        public event Action? AnimationToggleRequested;
        public event Action? SettingsRequested;
        public event Action? ExitRequested;
        public bool CanUseAnimatedIcons { get; set; } = true;
        public string? AnimationIconLoadError { get; set; }
        internal string? VisibilityText { get; private set; }
        internal string? InteractionText { get; private set; }
        internal string? AnimationText { get; private set; }
        internal int? CurrentAnimatedFrame { get; private set; }
        internal bool IsStatic { get; private set; }
        internal int ShowCount { get; private set; }
        internal int RecoveryCount { get; private set; }
        internal int DisposeCount { get; private set; }
        internal Exception? RecoveryException { get; set; }
        internal Exception? ShowException { get; set; }
        internal int DoubleClickSubscriberCount => DoubleClicked?.GetInvocationList().Length ?? 0;

        public void Show()
        {
            ShowCount++;
            if (ShowException is not null) throw ShowException;
        }
        public void SetMenuText(
            string visibilityText,
            string interactionText,
            string animationText)
        {
            VisibilityText = visibilityText;
            InteractionText = interactionText;
            AnimationText = animationText;
        }
        public void SetAnimatedFrame(int frameIndex)
        {
            CurrentAnimatedFrame = frameIndex;
            IsStatic = false;
        }
        public void SetStaticIcon() => IsStatic = true;
        public void RecoverAfterExplorerRestart()
        {
            RecoveryCount++;
            if (RecoveryException is not null) throw RecoveryException;
        }
        public void Dispose() => DisposeCount++;
        internal void FireDoubleClick() => DoubleClicked?.Invoke();
        internal void FireVisibilityToggle() => VisibilityToggleRequested?.Invoke();
        internal void FireInteractionToggle() => InteractionToggleRequested?.Invoke();
        internal void FireAnimationToggle() => AnimationToggleRequested?.Invoke();
        internal void FireSettings() => SettingsRequested?.Invoke();
        internal void FireExit() => ExitRequested?.Invoke();
    }

    private sealed class FakeRunCatAnimationController : IRunCatAnimationController
    {
        public int FrameCount => 8;
        public int FrameIndex { get; private set; }
        public TimeSpan Interval { get; private set; } = TimeSpan.FromMilliseconds(250);
        public bool IsRunning { get; private set; }
        public string? LastFault => null;
        public event Action<int>? FrameChanged;
        public event Action<string>? Faulted { add { } remove { } }
        public bool Start()
        {
            bool changed = !IsRunning;
            IsRunning = true;
            return changed;
        }
        public void Stop() => IsRunning = false;
        public bool UpdateInterval(TimeSpan interval)
        {
            bool changed = Interval != interval;
            Interval = interval;
            return changed;
        }
        public void Dispose()
        {
            FrameChanged = null;
        }
        internal void FireFrame(int frameIndex)
        {
            FrameIndex = frameIndex;
            FrameChanged?.Invoke(frameIndex);
        }
    }

    private sealed class FakeMessageApi : IRegisteredWindowMessageApi
    {
        internal int Message { get; } = 0xC123;
        internal int RegisterCount { get; private set; }
        public int Register(string messageName)
        {
            RegisterCount++;
            Assert.Equal(SystemTrayService.TaskbarCreatedMessageName, messageName);
            return Message;
        }
    }

    private sealed class FakeInteractionModeToggleAction : IInteractionModeToggleAction
    {
        public OverlayWindowState State { get; private set; } = new(
            OverlayInteractionMode.ClickThrough,
            OverlayInteractionMode.ClickThrough,
            true,
            false,
            null);
        public event Action<OverlayWindowState>? StateChanged;
        internal int RequestCount { get; private set; }
        internal bool FailToggle { get; set; }

        public void RequestToggle()
        {
            RequestCount++;
            OverlayInteractionMode mode = State.AppliedMode == OverlayInteractionMode.ClickThrough
                ? OverlayInteractionMode.Interactive
                : OverlayInteractionMode.ClickThrough;
            State = FailToggle
                ? State with
                {
                    RequestedMode = mode,
                    LastError = "configured mode failure"
                }
                : State with
                {
                    RequestedMode = mode,
                    AppliedMode = mode,
                    LastError = null
                };
            StateChanged?.Invoke(State);
        }

        public void RequestMode(OverlayInteractionMode mode)
        {
            State = State with { RequestedMode = mode, AppliedMode = mode };
            StateChanged?.Invoke(State);
        }
    }

    private sealed class SuccessfulNativeHotKeyApi : INativeGlobalHotKeyApi
    {
        public void Register(nint windowHandle, int identifier, uint modifiers, uint virtualKey) { }
        public void Unregister(nint windowHandle, int identifier) { }
    }
}
