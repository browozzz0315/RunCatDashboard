using Microsoft.Extensions.Logging;
using RunCatDashboard.App.Diagnostics;

namespace RunCatDashboard.App.Animation;

internal sealed class RunCatAnimationController : IRunCatAnimationController
{
    internal const int DefaultFrameCount = 8;

    private readonly object _gate = new();
    private readonly IAnimationTimer _timer;
    private readonly ILogger<RunCatAnimationController> _logger;
    private readonly FaultEpisodeTracker _faultEpisode = new();
    private bool _isRunning;
    private bool _startRequested;
    private bool _isDisposed;
    private long _generation;
    private int _frameIndex;
    private int _frameCount;
    private TimeSpan _interval = CpuAnimationSpeedMapper.SlowestInterval;
    private string? _lastFault;

    internal RunCatAnimationController(
        IAnimationTimer timer,
        int frameCount = DefaultFrameCount,
        ILogger<RunCatAnimationController>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(timer);
        ArgumentOutOfRangeException.ThrowIfLessThan(frameCount, 1);

        _timer = timer;
        _logger = logger ??
            Microsoft.Extensions.Logging.Abstractions.NullLogger<RunCatAnimationController>.Instance;
        _frameCount = frameCount;
    }

    public event Action<int>? FrameChanged;

    public event Action<string>? Faulted;

    public int FrameCount
    {
        get
        {
            lock (_gate)
            {
                return _frameCount;
            }
        }
    }

    public int FrameIndex
    {
        get
        {
            lock (_gate)
            {
                return _frameIndex;
            }
        }
    }

    public TimeSpan Interval
    {
        get
        {
            lock (_gate)
            {
                return _interval;
            }
        }
    }

    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _isRunning;
            }
        }
    }

    public string? LastFault
    {
        get
        {
            lock (_gate)
            {
                return _lastFault;
            }
        }
    }

    public bool Start()
    {
        long generation;
        TimeSpan interval;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            if (_isRunning)
            {
                _startRequested = true;
                return false;
            }

            _startRequested = true;
            if (_frameCount == 1)
            {
                return false;
            }

            _isRunning = true;
            generation = ++_generation;
            interval = _interval;
        }

        try
        {
            bool started = _timer.Start(
                interval,
                () => OnTick(generation),
                message => RecordFault(generation, message));
            if (!started)
            {
                throw new InvalidOperationException(
                    "The run-cat animation timer was already running unexpectedly.");
            }

            TryLog(() => _logger.LogDebug(
                "Run-cat animation started. {Operation} {Subsystem}",
                "StartAnimation",
                "Animation"));
            return true;
        }
        catch
        {
            lock (_gate)
            {
                if (generation == _generation)
                {
                    _isRunning = false;
                    _startRequested = false;
                    _generation++;
                }
            }

            throw;
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (!_isRunning)
            {
                _startRequested = false;
                return;
            }

            _isRunning = false;
            _startRequested = false;
            _generation++;
        }

        _timer.Stop();
        TryLog(() => _logger.LogDebug(
            "Run-cat animation stopped. {Operation} {Subsystem}",
            "StopAnimation",
            "Animation"));
    }

    public bool UpdateInterval(TimeSpan interval)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);

        bool isRunning;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            if (_interval == interval)
            {
                return false;
            }

            _interval = interval;
            isRunning = _isRunning;
        }

        if (isRunning)
        {
            try
            {
                _timer.UpdateInterval(interval);
            }
            catch (ObjectDisposedException) when (IsDisposed())
            {
                return false;
            }
        }

        return true;
    }

    public bool ReplaceFrameSet(int frameCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(frameCount, 1);

        Action<int>? handlers;
        bool shouldStart;
        bool shouldStop;
        long generation;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            _frameCount = frameCount;
            _frameIndex = 0;
            handlers = FrameChanged;
            shouldStop = _isRunning && frameCount == 1;
            if (shouldStop)
            {
                _isRunning = false;
                generation = ++_generation;
            }
            else
            {
                generation = _generation;
            }
            shouldStart = _startRequested && !_isRunning && frameCount > 1;
            if (shouldStart)
            {
                _isRunning = true;
                generation = ++_generation;
            }
        }

        if (shouldStop)
        {
            _timer.Stop();
        }

        if (shouldStart)
        {
            try
            {
                if (!_timer.Start(
                    Interval,
                    () => OnTick(generation),
                    message => RecordFault(generation, message)))
                {
                    throw new InvalidOperationException(
                        "The run-cat animation timer was already running unexpectedly.");
                }
            }
            catch
            {
                lock (_gate)
                {
                    if (generation == _generation)
                    {
                        _isRunning = false;
                        _generation++;
                    }
                }
                throw;
            }
        }

        PublishFrame(generation, 0, handlers);
        return true;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            _isRunning = false;
            _startRequested = false;
            _generation++;
        }

        _timer.Stop();
        _timer.Dispose();
    }

    private void OnTick(long generation)
    {
        int nextFrame;
        Action<int>? handlers;
        lock (_gate)
        {
            if (_isDisposed || !_isRunning || generation != _generation)
            {
                return;
            }

            _frameIndex = (_frameIndex + 1) % _frameCount;
            nextFrame = _frameIndex;
            handlers = FrameChanged;
        }

        PublishFrame(generation, nextFrame, handlers);
    }

    private void PublishFrame(
        long generation,
        int frameIndex,
        Action<int>? handlers)
    {
        lock (_gate)
        {
            if (_isDisposed || generation != _generation)
            {
                return;
            }
        }

        if (handlers is null)
        {
            ReportRecoveryIfNeeded();
            return;
        }

        bool didFault = false;
        foreach (Action<int> handler in handlers.GetInvocationList().Cast<Action<int>>())
        {
            try
            {
                handler(frameIndex);
            }
            catch (Exception exception)
            {
                didFault = true;
                RecordFault(
                    generation,
                    $"Publishing the run-cat frame failed: {exception.Message}",
                    exception);
            }
        }

        if (!didFault)
        {
            ReportRecoveryIfNeeded();
        }
    }

    private void RecordFault(long generation, string message, Exception? cause = null)
    {
        Action<string>? handlers;
        lock (_gate)
        {
            if (_isDisposed || !_isRunning || generation != _generation)
            {
                return;
            }

            _lastFault = message;
            handlers = Faulted;
        }

        if (_faultEpisode.Observe(isFaulted: true) == FaultEpisodeTransition.Failed)
        {
            try
            {
                if (cause is null)
                {
                    _logger.LogWarning(
                        "Run-cat animation entered a fault episode. {Operation} {Subsystem} {FaultState}",
                        "RunAnimation",
                        "Animation",
                        "Faulted");
                }
                else
                {
                    _logger.LogError(
                        cause,
                        "Run-cat animation entered a fault episode. {Operation} {Subsystem} {FaultState} {HResult}",
                        "PublishAnimationFrame",
                        "Animation",
                        "Faulted",
                        cause.HResult);
                }
            }
            catch
            {
            }
        }

        if (handlers is null)
        {
            return;
        }

        foreach (Action<string> handler in handlers.GetInvocationList().Cast<Action<string>>())
        {
            try
            {
                handler(message);
            }
            catch (Exception exception)
            {
                lock (_gate)
                {
                    if (!_isDisposed && generation == _generation)
                    {
                        _lastFault =
                            $"{message} Publishing the animation fault also failed: {exception.Message}";
                    }
                }
            }
        }
    }

    private void ReportRecoveryIfNeeded()
    {
        if (_faultEpisode.Observe(isFaulted: false) != FaultEpisodeTransition.Recovered)
        {
            return;
        }

        try
        {
            _logger.LogInformation(
                "Run-cat animation recovered. {Operation} {Subsystem} {FaultState}",
                "RunAnimation",
                "Animation",
                "Recovered");
        }
        catch
        {
        }
    }

    private bool IsDisposed()
    {
        lock (_gate)
        {
            return _isDisposed;
        }
    }

    private static void TryLog(Action log)
    {
        try
        {
            log();
        }
        catch
        {
            // Logging must not alter animation lifecycle or fault state.
        }
    }
}
