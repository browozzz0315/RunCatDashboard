using RunCatDashboard.App.Animation;

namespace RunCatDashboard.Tests.Animation;

public sealed class RunCatAnimationControllerTests
{
    [Fact]
    public void Constructor_WithEmptyFrameSequence_ThrowsClearError()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RunCatAnimationController(new FakeAnimationTimer(), 0));
    }

    [Fact]
    public void SixFrames_AdvanceInOrderAndWrapToFirstFrame()
    {
        var timer = new FakeAnimationTimer();
        using var controller = new RunCatAnimationController(timer, 6);
        var published = new List<int>();
        controller.FrameChanged += published.Add;
        controller.Start();

        for (int index = 0; index < 6; index++)
        {
            timer.Fire();
        }

        Assert.Equal([1, 2, 3, 4, 5, 0], published);
        Assert.Equal(0, controller.FrameIndex);
    }

    [Fact]
    public void Stop_PreventsCurrentAndDelayedCallbacksFromAdvancing()
    {
        var timer = new FakeAnimationTimer();
        using var controller = new RunCatAnimationController(timer);
        int publications = 0;
        controller.FrameChanged += _ => publications++;
        controller.Start();
        timer.Fire();
        controller.Stop();

        timer.Fire();
        timer.FireSavedCallback();

        Assert.Equal(1, publications);
        Assert.Equal(1, controller.FrameIndex);
        Assert.False(controller.IsRunning);
    }

    [Fact]
    public void Stop_WhenRepeated_CallsUnderlyingTimerOnlyOnce()
    {
        var timer = new FakeAnimationTimer();
        var controller = new RunCatAnimationController(timer);
        controller.Start();

        controller.Stop();
        controller.Stop();

        Assert.Equal(1, timer.StopCount);
        controller.Dispose();
    }

    [Fact]
    public void Restart_ContinuesFromCurrentFrameWithoutDuplicateTimer()
    {
        var timer = new FakeAnimationTimer();
        using var controller = new RunCatAnimationController(timer);
        controller.Start();
        Assert.False(controller.Start());
        timer.Fire();
        controller.Stop();

        Assert.True(controller.Start());
        timer.Fire();

        Assert.Equal(2, controller.FrameIndex);
        Assert.Equal(2, timer.StartCount);
        Assert.Equal(1, timer.MaximumConcurrentStarts);
    }

    [Fact]
    public void StopAndDispose_AreIdempotentAndDisposePreventsPublicationOrRestart()
    {
        var timer = new FakeAnimationTimer();
        var controller = new RunCatAnimationController(timer);
        int publications = 0;
        controller.FrameChanged += _ => publications++;
        controller.Start();
        controller.Stop();
        controller.Stop();
        controller.Dispose();
        controller.Dispose();

        timer.FireSavedCallback();

        Assert.Equal(0, publications);
        Assert.Equal(1, timer.DisposeCount);
        Assert.Throws<ObjectDisposedException>(() => controller.Start());
    }

    [Fact]
    public void UpdateInterval_AppliesWithoutRestartingTimer()
    {
        var timer = new FakeAnimationTimer();
        using var controller = new RunCatAnimationController(timer);
        controller.Start();

        Assert.True(controller.UpdateInterval(TimeSpan.FromMilliseconds(125)));
        Assert.False(controller.UpdateInterval(TimeSpan.FromMilliseconds(125)));

        Assert.Equal(TimeSpan.FromMilliseconds(125), timer.Interval);
        Assert.Equal(1, timer.StartCount);
        Assert.Equal(1, timer.UpdateCount);
    }

    [Fact]
    public void UpdateInterval_BeforeStart_IsUsedByFirstTimerStart()
    {
        var timer = new FakeAnimationTimer();
        using var controller = new RunCatAnimationController(timer);
        controller.UpdateInterval(TimeSpan.FromMilliseconds(80));

        controller.Start();

        Assert.Equal(TimeSpan.FromMilliseconds(80), timer.Interval);
        Assert.Equal(0, timer.UpdateCount);
    }

    [Fact]
    public void SingleFrame_DoesNotStartUnnecessaryTimerOrPublishFrames()
    {
        var timer = new FakeAnimationTimer();
        using var controller = new RunCatAnimationController(timer, 1);
        int publications = 0;
        controller.FrameChanged += _ => publications++;

        Assert.False(controller.Start());
        timer.FireSavedCallback();

        Assert.Equal(0, timer.StartCount);
        Assert.Equal(0, publications);
        Assert.False(controller.IsRunning);
    }

    [Fact]
    public void SubscriberException_IsContainedAndPublishedAsDiagnosticFault()
    {
        var timer = new FakeAnimationTimer();
        using var controller = new RunCatAnimationController(timer);
        string? diagnostic = null;
        controller.FrameChanged += _ => throw new InvalidOperationException("configured subscriber failure");
        controller.Faulted += message => diagnostic = message;
        controller.Start();

        Exception? exception = Record.Exception(timer.Fire);

        Assert.Null(exception);
        Assert.Contains("configured subscriber failure", controller.LastFault);
        Assert.Equal(controller.LastFault, diagnostic);
    }

    [Fact]
    public void TimerFault_IsDiagnosticAndDoesNotEscapeCallbackBoundary()
    {
        var timer = new FakeAnimationTimer();
        using var controller = new RunCatAnimationController(timer);
        controller.Start();

        timer.FireFault("configured timer failure");

        Assert.Equal("configured timer failure", controller.LastFault);
    }

    private sealed class FakeAnimationTimer : IAnimationTimer
    {
        private Action? _callback;
        private Action? _savedCallback;
        private Action<string>? _faultCallback;
        private bool _isRunning;
        private int _concurrentStarts;

        internal int StartCount { get; private set; }
        internal int UpdateCount { get; private set; }
        internal int StopCount { get; private set; }
        internal int DisposeCount { get; private set; }
        internal int MaximumConcurrentStarts { get; private set; }
        internal TimeSpan Interval { get; private set; }

        public bool Start(TimeSpan interval, Action callback, Action<string> faultCallback)
        {
            StartCount++;
            if (_isRunning)
            {
                return false;
            }

            _isRunning = true;
            _concurrentStarts++;
            MaximumConcurrentStarts = Math.Max(MaximumConcurrentStarts, _concurrentStarts);
            Interval = interval;
            _callback = callback;
            _savedCallback = callback;
            _faultCallback = faultCallback;
            return true;
        }

        public bool UpdateInterval(TimeSpan interval)
        {
            if (Interval == interval)
            {
                return false;
            }

            UpdateCount++;
            Interval = interval;
            return true;
        }

        public void Stop()
        {
            StopCount++;
            if (_isRunning)
            {
                _concurrentStarts--;
            }

            _isRunning = false;
            _callback = null;
            _faultCallback = null;
        }

        public void Dispose()
        {
            DisposeCount++;
            Stop();
        }

        internal void Fire() => _callback?.Invoke();
        internal void FireSavedCallback() => _savedCallback?.Invoke();
        internal void FireFault(string message) => _faultCallback?.Invoke(message);
    }
}
