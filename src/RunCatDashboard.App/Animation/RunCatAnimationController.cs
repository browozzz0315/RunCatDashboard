namespace RunCatDashboard.App.Animation;

internal sealed class RunCatAnimationController : IRunCatAnimationController
{
    internal const int DefaultFrameCount = 8;

    private readonly object _gate = new();
    private readonly IAnimationTimer _timer;
    private bool _isRunning;
    private bool _isDisposed;
    private long _generation;
    private int _frameIndex;
    private TimeSpan _interval = CpuAnimationSpeedMapper.SlowestInterval;
    private string? _lastFault;

    internal RunCatAnimationController(
        IAnimationTimer timer,
        int frameCount = DefaultFrameCount)
    {
        ArgumentNullException.ThrowIfNull(timer);
        ArgumentOutOfRangeException.ThrowIfLessThan(frameCount, 1);

        _timer = timer;
        FrameCount = frameCount;
    }

    public event Action<int>? FrameChanged;

    public event Action<string>? Faulted;

    public int FrameCount { get; }

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
            if (_isRunning || FrameCount == 1)
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

            return true;
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

    public void Stop()
    {
        lock (_gate)
        {
            if (!_isRunning)
            {
                return;
            }

            _isRunning = false;
            _generation++;
        }

        _timer.Stop();
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
            _timer.UpdateInterval(interval);
        }

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

            _frameIndex = (_frameIndex + 1) % FrameCount;
            nextFrame = _frameIndex;
            handlers = FrameChanged;
        }

        if (handlers is null)
        {
            return;
        }

        foreach (Action<int> handler in handlers.GetInvocationList().Cast<Action<int>>())
        {
            try
            {
                handler(nextFrame);
            }
            catch (Exception exception)
            {
                RecordFault(
                    generation,
                    $"Publishing the run-cat frame failed: {exception.Message}");
            }
        }
    }

    private void RecordFault(long generation, string message)
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
}
