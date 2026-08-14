using RunCatDashboard.App.Animation;
using Microsoft.Extensions.Logging;
using RunCatDashboard.Tests.Diagnostics;
using RunCatDashboard.App.Windowing;
using RunCatDashboard.App.Settings;
using RunCatDashboard.App.Theming;

namespace RunCatDashboard.Tests.Windowing;

public sealed class TrayAnimationCoordinatorTests
{
    [Fact]
    public void Initialize_DefaultsToAnimatedAndUsesCurrentSharedFrame()
    {
        var adapter = new FakeTrayIconAdapter();
        var animation = new FakeRunCatAnimationController { FrameIndex = 4 };
        using var coordinator = new TrayAnimationCoordinator(adapter, animation);

        Assert.True(coordinator.Initialize());
        Assert.False(coordinator.Initialize());

        Assert.True(coordinator.IsAnimated);
        Assert.Equal(4, adapter.LastAnimatedFrame);
        Assert.Equal(1, adapter.AnimatedUpdateCount);
        Assert.Equal(1, animation.FrameChangedSubscriberCount);
    }

    [Fact]
    public void ToggleMode_ChangesOnlyTrayPresentationAndCanResumeCurrentFrame()
    {
        var adapter = new FakeTrayIconAdapter();
        var animation = new FakeRunCatAnimationController { FrameIndex = 2 };
        using var coordinator = new TrayAnimationCoordinator(adapter, animation);
        coordinator.Initialize();

        Assert.False(coordinator.ToggleMode());
        animation.FireFrame(3);

        Assert.True(adapter.IsStatic);
        Assert.Equal(1, adapter.AnimatedUpdateCount);

        Assert.True(coordinator.ToggleMode());

        Assert.Equal(3, adapter.LastAnimatedFrame);
        Assert.Equal(0, animation.StartCount);
        Assert.Equal(0, animation.StopCount);
    }

    [Fact]
    public void SharedFrameEvent_UpdatesTrayOnceEvenWhenDashboardVisibilityIsHidden()
    {
        var adapter = new FakeTrayIconAdapter();
        var animation = new FakeRunCatAnimationController();
        using var coordinator = new TrayAnimationCoordinator(adapter, animation);
        using var visibility = new WindowVisibilityCoordinator();
        coordinator.Initialize();
        visibility.SetUserRequestedVisibility(false);

        animation.FireFrame(5);

        Assert.False(visibility.State.IsActuallyVisible);
        Assert.Equal(5, adapter.LastAnimatedFrame);
        Assert.Equal(2, adapter.AnimatedUpdateCount);
    }

    [Fact]
    public void FrameFailure_RetainsPreviousIconAndPublishesDiagnostic()
    {
        var adapter = new FakeTrayIconAdapter();
        var animation = new FakeRunCatAnimationController();
        var logger = new RecordingLogger<TrayAnimationCoordinator>();
        using var coordinator = new TrayAnimationCoordinator(adapter, animation, logger);
        coordinator.Initialize();
        adapter.FrameFailure = new InvalidOperationException("configured frame failure");

        animation.FireFrame(6);
        animation.FireFrame(7);
        adapter.FrameFailure = null;
        animation.FireFrame(1);

        Assert.Equal(1, adapter.LastAnimatedFrame);
        Assert.Null(coordinator.LastError);
        Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Error);
        Assert.Single(logger.Entries, entry =>
            entry.Level == LogLevel.Information &&
            entry.Properties.TryGetValue("FaultState", out object? state) &&
            Equals(state, "Recovered"));
    }

    [Fact]
    public void AnimationSetLoadFailure_FallsBackToStaticAndRetainsDiagnostic()
    {
        var adapter = new FakeTrayIconAdapter
        {
            CanUseAnimatedIcons = false,
            AnimationIconLoadError = "configured resource failure"
        };
        using var coordinator = new TrayAnimationCoordinator(
            adapter,
            new FakeRunCatAnimationController());

        coordinator.Initialize();

        Assert.True(coordinator.IsAnimated);
        Assert.True(adapter.IsStatic);
        Assert.Contains("已切換為靜態圖示", coordinator.LastError);
        Assert.DoesNotContain("configured resource failure", coordinator.LastError);
    }

    [Fact]
    public void RestoreCurrentModeIcon_ReappliesAnimatedOrStaticSelection()
    {
        var adapter = new FakeTrayIconAdapter();
        var animation = new FakeRunCatAnimationController { FrameIndex = 1 };
        using var coordinator = new TrayAnimationCoordinator(adapter, animation);
        coordinator.Initialize();
        animation.FireFrame(7);

        coordinator.RestoreCurrentModeIcon();
        Assert.Equal(7, adapter.LastAnimatedFrame);

        coordinator.ToggleMode();
        adapter.IsStatic = false;
        coordinator.RestoreCurrentModeIcon();
        Assert.True(adapter.IsStatic);
    }

    [Fact]
    public void Dispose_IsIdempotentAndStopsAllLaterFrameUpdates()
    {
        var adapter = new FakeTrayIconAdapter();
        var animation = new FakeRunCatAnimationController();
        var coordinator = new TrayAnimationCoordinator(adapter, animation);
        coordinator.Initialize();

        coordinator.Dispose();
        coordinator.Dispose();
        animation.FireFrame(2);

        Assert.Equal(1, adapter.AnimatedUpdateCount);
        Assert.Equal(0, animation.FrameChangedSubscriberCount);
        Assert.Throws<ObjectDisposedException>(() => coordinator.ToggleMode());
    }

    [Fact]
    public void ResolvedThemeChanged_SwitchesTraySourceWithoutResettingFrame()
    {
        var adapter = new FakeTrayIconAdapter();
        var animation = new FakeRunCatAnimationController { FrameIndex = 5 };
        var theme = new FakeThemeCoordinator(ResolvedTheme.Light);
        using var coordinator = new TrayAnimationCoordinator(
            adapter,
            animation,
            themeCoordinator: theme);

        coordinator.Initialize();
        theme.Publish(ResolvedTheme.Dark);

        Assert.Equal(ResolvedTheme.Dark, adapter.ResolvedTheme);
        Assert.Equal(5, adapter.LastAnimatedFrame);
        Assert.Equal(2, adapter.AnimatedUpdateCount);
        Assert.Equal(0, animation.StartCount);
        Assert.Equal(0, animation.StopCount);
    }

    [Fact]
    public void Initialize_UsesCurrentResolvedThemeForInitialTrayIcon()
    {
        var adapter = new FakeTrayIconAdapter();
        var animation = new FakeRunCatAnimationController { FrameIndex = 2 };
        var theme = new FakeThemeCoordinator(ResolvedTheme.Dark);
        using var coordinator = new TrayAnimationCoordinator(
            adapter,
            animation,
            themeCoordinator: theme);

        coordinator.Initialize();

        Assert.Equal(ResolvedTheme.Dark, adapter.ResolvedTheme);
        Assert.Equal(2, adapter.LastAnimatedFrame);
    }

    [Fact]
    public void ApplicationAssembly_HasOnlyOneAnimationTimerImplementation()
    {
        Type[] implementations = typeof(IRunCatAnimationController).Assembly
            .GetTypes()
            .Where(type =>
                typeof(IAnimationTimer).IsAssignableFrom(type) &&
                type is { IsInterface: false, IsAbstract: false })
            .ToArray();

        Assert.Single(implementations);
        Assert.Equal(typeof(DispatcherAnimationTimer), implementations[0]);
    }

    private sealed class FakeTrayIconAdapter : ITrayIconAdapter
    {
        public event Action? DoubleClicked { add { } remove { } }
        public event Action? VisibilityToggleRequested { add { } remove { } }
        public event Action? InteractionToggleRequested { add { } remove { } }
        public event Action? AnimationToggleRequested { add { } remove { } }
        public event Action? SettingsRequested { add { } remove { } }
        public event Action? ExitRequested { add { } remove { } }
        public bool CanUseAnimatedIcons { get; set; } = true;
        public string? AnimationIconLoadError { get; set; }
        internal ResolvedTheme ResolvedTheme { get; private set; } = ResolvedTheme.Light;
        internal int? LastAnimatedFrame { get; private set; }
        internal int AnimatedUpdateCount { get; private set; }
        internal bool IsStatic { get; set; }
        internal Exception? FrameFailure { get; set; }

        public void SetResolvedTheme(ResolvedTheme theme) => ResolvedTheme = theme;

        public void SetAnimatedFrame(int frameIndex)
        {
            if (FrameFailure is not null)
            {
                throw FrameFailure;
            }

            LastAnimatedFrame = frameIndex;
            AnimatedUpdateCount++;
            IsStatic = false;
        }

        public void SetStaticIcon() => IsStatic = true;
        public void Show() { }
        public void SetMenuText(string visibilityText, string interactionText, string animationText) { }
        public void RecoverAfterExplorerRestart() { }
        public void Dispose() { }
    }

    private sealed class FakeThemeCoordinator(ResolvedTheme initialTheme) : IThemeCoordinator
    {
        public ThemePreference ThemePreference => ThemePreference.System;
        public ResolvedTheme ResolvedTheme { get; private set; } = initialTheme;
        public event Action<ResolvedTheme>? ResolvedThemeChanged;

        public Task ApplyPreferenceAsync(
            ThemePreference preference,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public void Publish(ResolvedTheme theme)
        {
            ResolvedTheme = theme;
            ResolvedThemeChanged?.Invoke(theme);
        }

        public void Dispose() => ResolvedThemeChanged = null;
    }

    private sealed class FakeRunCatAnimationController : IRunCatAnimationController
    {
        private Action<int>? _frameChanged;
        public int FrameCount => 8;
        public int FrameIndex { get; set; }
        public TimeSpan Interval { get; private set; } = TimeSpan.FromMilliseconds(250);
        public bool IsRunning { get; private set; }
        public string? LastFault => null;
        public event Action<int>? FrameChanged
        {
            add => _frameChanged += value;
            remove => _frameChanged -= value;
        }
        public event Action<string>? Faulted { add { } remove { } }
        internal int FrameChangedSubscriberCount =>
            _frameChanged?.GetInvocationList().Length ?? 0;
        internal int StartCount { get; private set; }
        internal int StopCount { get; private set; }

        public bool Start()
        {
            StartCount++;
            IsRunning = true;
            return true;
        }

        public void Stop()
        {
            StopCount++;
            IsRunning = false;
        }

        public bool UpdateInterval(TimeSpan interval)
        {
            Interval = interval;
            return true;
        }

        public void Dispose()
        {
            _frameChanged = null;
        }

        internal void FireFrame(int frameIndex)
        {
            FrameIndex = frameIndex;
            _frameChanged?.Invoke(frameIndex);
        }
    }
}
