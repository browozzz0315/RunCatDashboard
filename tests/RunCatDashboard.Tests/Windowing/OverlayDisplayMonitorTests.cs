using System.Collections.Concurrent;
using RunCatDashboard.App.Windowing;

namespace RunCatDashboard.Tests.Windowing;

public sealed class OverlayDisplayMonitorTests
{
    private static readonly nint OverlayWindow = new(1234);

    [Fact]
    public void Start_WhenRepeated_RegistersHookAndTimerOnce()
    {
        var fixture = new MonitorFixture();

        Assert.True(fixture.Monitor.Start(OverlayWindow));
        Assert.False(fixture.Monitor.Start(OverlayWindow));

        Assert.Equal(1, fixture.Hook.StartCount);
        Assert.Equal(1, fixture.Timer.StartCount);
        Assert.Equal(TimeSpan.FromSeconds(1), fixture.Timer.Interval);
    }

    [Fact]
    public void StopAndDispose_WhenRepeated_CleanUpOnce()
    {
        var fixture = new MonitorFixture();
        fixture.Monitor.Start(OverlayWindow);

        fixture.Monitor.Stop();
        fixture.Monitor.Stop();
        fixture.Monitor.Dispose();
        fixture.Monitor.Dispose();

        Assert.Equal(1, fixture.Hook.StopCount);
        Assert.Equal(1, fixture.Timer.StopCount);
        Assert.Equal(1, fixture.Hook.DisposeCount);
        Assert.Equal(1, fixture.Timer.DisposeCount);
    }

    [Fact]
    public void Callbacks_AfterStop_DoNotObserveOrPublish()
    {
        var fixture = new MonitorFixture();
        int publishCount = 0;
        fixture.Monitor.StateChanged += _ => publishCount++;
        fixture.Monitor.Start(OverlayWindow);
        int observationsBeforeStop = fixture.Source.ObserveCount;
        int publicationsBeforeStop = publishCount;

        fixture.Monitor.Stop();
        fixture.Hook.FireSavedCallback();
        fixture.Timer.FireSavedCallback();

        Assert.Equal(observationsBeforeStop, fixture.Source.ObserveCount);
        Assert.Equal(publicationsBeforeStop, publishCount);
    }

    [Fact]
    public void ForegroundAndTimerCallbacks_ReevaluateImmediately()
    {
        var fixture = new MonitorFixture();
        fixture.Monitor.Start(OverlayWindow);
        int initialCount = fixture.Source.ObserveCount;

        fixture.Hook.Fire();
        fixture.Timer.Fire();

        Assert.Equal(initialCount + 2, fixture.Source.ObserveCount);
    }

    [Fact]
    public void DisplaySettingsAndOverlayMonitorNotifications_ReevaluateImmediately()
    {
        var fixture = new MonitorFixture();
        fixture.Monitor.Start(OverlayWindow);
        int initialCount = fixture.Source.ObserveCount;

        Assert.True(fixture.Monitor.NotifyDisplaySettingsChanged());
        Assert.True(fixture.Monitor.NotifyOverlayMonitorChanged());

        Assert.Equal(initialCount + 2, fixture.Source.ObserveCount);
    }

    [Fact]
    public void PolicySwitch_RecalculatesAndPublishesImmediately()
    {
        var fixture = new MonitorFixture
        {
            Observation = Observation(fullscreen: true, sameMonitor: true)
        };
        fixture.Monitor.Start(OverlayWindow);
        Assert.False(fixture.Monitor.State.IsVisible);
        int observationCount = fixture.Source.ObserveCount;

        Assert.True(fixture.Monitor.SetPolicy(OverlayDisplayPolicy.AlwaysOnTop));

        Assert.True(fixture.Monitor.State.IsVisible);
        Assert.True(fixture.Monitor.State.IsTopmost);
        Assert.Equal(observationCount, fixture.Source.ObserveCount);
    }

    [Fact]
    public void IdenticalObservation_DoesNotPublishRepeatedState()
    {
        var fixture = new MonitorFixture();
        int publishCount = 0;
        fixture.Monitor.StateChanged += _ => publishCount++;
        fixture.Monitor.Start(OverlayWindow);
        int initialPublishCount = publishCount;

        fixture.Timer.Fire();
        fixture.Timer.Fire();

        Assert.Equal(initialPublishCount, publishCount);
    }

    [Fact]
    public void TransientFault_IsFailVisibleAndRecoversOnNextObservation()
    {
        var fixture = new MonitorFixture
        {
            Observation = Observation(fullscreen: true, sameMonitor: true) with
            {
                Fault = "temporary detector failure"
            }
        };
        fixture.Monitor.Start(OverlayWindow);

        Assert.True(fixture.Monitor.State.IsVisible);
        Assert.NotNull(fixture.Monitor.State.Fault);

        fixture.Observation = Observation(fullscreen: true, sameMonitor: true);
        fixture.Timer.Fire();

        Assert.False(fixture.Monitor.State.IsVisible);
        Assert.Null(fixture.Monitor.State.Fault);
    }

    [Fact]
    public void LifecycleAndCallbackFailures_AreObservableAndFailVisible()
    {
        var fixture = new MonitorFixture
        {
            Observation = Observation(fullscreen: true, sameMonitor: true)
        };
        fixture.Hook.StartException = new InvalidOperationException("hook unavailable");

        fixture.Monitor.Start(OverlayWindow);

        Assert.True(fixture.Monitor.State.IsVisible);
        Assert.Contains("hook unavailable", fixture.Monitor.State.Fault);

        fixture.Timer.FireFault("configured timer callback failure");

        Assert.True(fixture.Monitor.State.IsVisible);
        Assert.Contains("configured timer callback failure", fixture.Monitor.State.Fault);
    }

    [Fact]
    public void SubscriberFailure_IsCapturedAsFailVisibleDiagnosticState()
    {
        var fixture = new MonitorFixture
        {
            Observation = Observation(fullscreen: true, sameMonitor: true)
        };
        fixture.Monitor.StateChanged += _ =>
            throw new InvalidOperationException("configured subscriber failure");

        Exception? exception = Record.Exception(() => fixture.Monitor.Start(OverlayWindow));

        Assert.Null(exception);
        Assert.True(fixture.Monitor.State.IsVisible);
        Assert.Contains("configured subscriber failure", fixture.Monitor.State.Fault);
    }

    [Fact]
    public void Subscriber_IsInvokedWithoutHoldingStateGate()
    {
        var fixture = new MonitorFixture();
        bool concurrentReadCompleted = false;
        fixture.Monitor.StateChanged += _ =>
        {
            Task<OverlayDisplayPolicyState> readState =
                Task.Run(() => fixture.Monitor.State);
            concurrentReadCompleted = readState.Wait(TimeSpan.FromSeconds(5));
        };

        fixture.Monitor.Start(OverlayWindow);

        Assert.True(concurrentReadCompleted);
    }

    [Fact]
    public void StopFailure_IsRetainedInObservableStateWithoutPublishingAfterStop()
    {
        var fixture = new MonitorFixture();
        int publishCount = 0;
        fixture.Monitor.StateChanged += _ => publishCount++;
        fixture.Monitor.Start(OverlayWindow);
        int publicationsBeforeStop = publishCount;
        fixture.Hook.StopException = new InvalidOperationException("unhook failed");

        fixture.Monitor.Stop();

        Assert.Contains("unhook failed", fixture.Monitor.State.Fault);
        Assert.True(fixture.Monitor.State.IsVisible);
        Assert.Equal(publicationsBeforeStop, publishCount);
        Assert.Equal(1, fixture.Timer.StopCount);
    }

    [Fact]
    public void Reevaluate_DoesNotChangeOverlayHandleOrUnrelatedSubsystems()
    {
        var fixture = new MonitorFixture();
        int samplingStartCount = 1;
        int hotKeyRegistrationCount = 1;
        int interactionModeChangeCount = 0;
        fixture.Monitor.Start(OverlayWindow);

        fixture.Observation = Observation(fullscreen: true, sameMonitor: true);
        fixture.Monitor.Reevaluate();
        fixture.Observation = Observation(fullscreen: false, sameMonitor: false);
        fixture.Monitor.Reevaluate();

        Assert.All(fixture.Source.ObservedHandles, handle => Assert.Equal(OverlayWindow, handle));
        Assert.Equal(1, samplingStartCount);
        Assert.Equal(1, hotKeyRegistrationCount);
        Assert.Equal(0, interactionModeChangeCount);
    }

    [Fact]
    public async Task ConcurrentEvaluations_OlderCompletionCannotOverwriteNewerResult()
    {
        var source = new ConcurrentObservationSource();
        var hook = new FakeForegroundHook();
        var timer = new FakeTimer();
        var monitor = new OverlayDisplayMonitor(source, hook, timer);
        var published = new ConcurrentQueue<OverlayDisplayPolicyState>();
        monitor.StateChanged += state => published.Enqueue(state);
        monitor.Start(OverlayWindow);
        var older = new BlockingObservation(
            Observation(fullscreen: true, sameMonitor: true) with
            {
                ForegroundDiagnostic = "older observation"
            });
        source.Enqueue(() => older.Observe());
        source.Enqueue(() => Observation(fullscreen: false, sameMonitor: false) with
        {
            ForegroundDiagnostic = "newer observation"
        });

        Task olderEvaluation = Task.Run(() => monitor.Reevaluate());
        await older.WaitUntilStartedAsync();
        Task newerEvaluation = Task.Run(() => monitor.Reevaluate());
        await newerEvaluation.WaitAsync(TimeSpan.FromSeconds(5));
        older.Release();
        await olderEvaluation.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(monitor.State.IsVisible);
        Assert.Equal("newer observation", monitor.State.ForegroundDiagnostic);
        Assert.DoesNotContain(
            published,
            state => state.ForegroundDiagnostic == "older observation");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DelayedEvaluation_AfterStopOrDispose_DoesNotPublish(bool dispose)
    {
        var source = new ConcurrentObservationSource();
        var hook = new FakeForegroundHook();
        var timer = new FakeTimer();
        var monitor = new OverlayDisplayMonitor(source, hook, timer);
        int publishCount = 0;
        monitor.StateChanged += _ => Interlocked.Increment(ref publishCount);
        monitor.Start(OverlayWindow);
        var delayed = new BlockingObservation(
            Observation(fullscreen: true, sameMonitor: true) with
            {
                ForegroundDiagnostic = "delayed observation"
            });
        source.Enqueue(() => delayed.Observe());
        Task evaluation = Task.Run(() => monitor.Reevaluate());
        await delayed.WaitUntilStartedAsync();
        int publicationsBeforeStop = Volatile.Read(ref publishCount);

        if (dispose)
        {
            monitor.Dispose();
        }
        else
        {
            monitor.Stop();
        }

        delayed.Release();
        await evaluation.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(publicationsBeforeStop, Volatile.Read(ref publishCount));
        Assert.DoesNotContain("delayed observation", monitor.State.ForegroundDiagnostic);
    }

    private static FullscreenObservation Observation(bool fullscreen, bool sameMonitor) =>
        new(fullscreen, sameMonitor, "foreground", "overlay monitor", null);

    private sealed class MonitorFixture
    {
        internal FakeObservationSource Source { get; } = new();
        internal FakeForegroundHook Hook { get; } = new();
        internal FakeTimer Timer { get; } = new();
        internal OverlayDisplayMonitor Monitor { get; }

        internal MonitorFixture()
        {
            Monitor = new OverlayDisplayMonitor(Source, Hook, Timer);
        }

        internal FullscreenObservation Observation
        {
            set => Source.Observation = value;
        }
    }

    private sealed class FakeObservationSource : IFullscreenObservationSource
    {
        internal FullscreenObservation Observation { get; set; } =
            OverlayDisplayMonitorTests.Observation(false, false);
        internal int ObserveCount { get; private set; }
        internal List<nint> ObservedHandles { get; } = [];

        public FullscreenObservation Observe(nint overlayWindowHandle)
        {
            ObserveCount++;
            ObservedHandles.Add(overlayWindowHandle);
            return Observation;
        }
    }

    private sealed class ConcurrentObservationSource : IFullscreenObservationSource
    {
        private readonly ConcurrentQueue<Func<FullscreenObservation>> _observations = [];

        internal void Enqueue(Func<FullscreenObservation> observation)
        {
            _observations.Enqueue(observation);
        }

        public FullscreenObservation Observe(nint overlayWindowHandle)
        {
            return _observations.TryDequeue(out Func<FullscreenObservation>? observation)
                ? observation()
                : OverlayDisplayMonitorTests.Observation(false, false);
        }
    }

    private sealed class BlockingObservation(FullscreenObservation result)
    {
        private readonly TaskCompletionSource _started =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ManualResetEventSlim _release = new(false);

        internal FullscreenObservation Observe()
        {
            _started.TrySetResult();
            Assert.True(_release.Wait(TimeSpan.FromSeconds(5)));
            return result;
        }

        internal Task WaitUntilStartedAsync() =>
            _started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        internal void Release() => _release.Set();
    }

    private sealed class FakeForegroundHook : IForegroundWindowEventHook
    {
        private Action? _callback;
        private Action<string>? _faultCallback;
        private Action? _savedCallback;
        internal int StartCount { get; private set; }
        internal int StopCount { get; private set; }
        internal int DisposeCount { get; private set; }
        internal Exception? StartException { get; set; }
        internal Exception? StopException { get; set; }

        public bool Start(Action callback, Action<string> faultCallback)
        {
            StartCount++;
            if (StartException is not null)
            {
                throw StartException;
            }

            _callback = callback;
            _savedCallback = callback;
            _faultCallback = faultCallback;
            return true;
        }

        public void Stop()
        {
            StopCount++;
            _callback = null;
            _faultCallback = null;
            if (StopException is not null)
            {
                throw StopException;
            }
        }

        public void Dispose() => DisposeCount++;

        internal void Fire() => _callback?.Invoke();

        internal void FireSavedCallback() => _savedCallback?.Invoke();
    }

    private sealed class FakeTimer : IReconciliationTimer
    {
        private Action? _callback;
        private Action<string>? _faultCallback;
        private Action? _savedCallback;
        internal int StartCount { get; private set; }
        internal int StopCount { get; private set; }
        internal int DisposeCount { get; private set; }
        internal TimeSpan Interval { get; private set; }

        public bool Start(
            TimeSpan interval,
            Action callback,
            Action<string> faultCallback)
        {
            StartCount++;
            Interval = interval;
            _callback = callback;
            _savedCallback = callback;
            _faultCallback = faultCallback;
            return true;
        }

        public void Stop()
        {
            StopCount++;
            _callback = null;
            _faultCallback = null;
        }

        public void Dispose() => DisposeCount++;

        internal void Fire() => _callback?.Invoke();

        internal void FireSavedCallback() => _savedCallback?.Invoke();

        internal void FireFault(string message) => _faultCallback?.Invoke(message);
    }
}
