using System.Windows.Threading;

namespace RunCatDashboard.App.Animation;

internal sealed class DispatcherAnimationTimer : IAnimationTimer
{
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _timer;
    private Action? _callback;
    private Action<string>? _faultCallback;
    private bool _isRunning;
    private bool _isDisposed;

    internal DispatcherAnimationTimer(Dispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        dispatcher.VerifyAccess();
        _dispatcher = dispatcher;
        _timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher);
        _timer.Tick += OnTick;
    }

    public bool Start(
        TimeSpan interval,
        Action callback,
        Action<string> faultCallback)
    {
        _dispatcher.VerifyAccess();
        ValidateInterval(interval);
        ArgumentNullException.ThrowIfNull(callback);
        ArgumentNullException.ThrowIfNull(faultCallback);
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (_isRunning)
        {
            return false;
        }

        _callback = callback;
        _faultCallback = faultCallback;
        _timer.Interval = interval;
        _timer.Start();
        _isRunning = true;
        return true;
    }

    public bool UpdateInterval(TimeSpan interval)
    {
        _dispatcher.VerifyAccess();
        ValidateInterval(interval);
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (_timer.Interval == interval)
        {
            return false;
        }

        _timer.Interval = interval;
        return true;
    }

    public void Stop()
    {
        _dispatcher.VerifyAccess();
        if (!_isRunning)
        {
            return;
        }

        _isRunning = false;
        _timer.Stop();
        _callback = null;
        _faultCallback = null;
    }

    public void Dispose()
    {
        _dispatcher.VerifyAccess();
        if (_isDisposed)
        {
            return;
        }

        Stop();
        _timer.Tick -= OnTick;
        _isDisposed = true;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        try
        {
            _callback?.Invoke();
        }
        catch (Exception exception)
        {
            try
            {
                _faultCallback?.Invoke(
                    $"Run-cat animation timer callback failed: {exception.Message}");
            }
            catch (Exception)
            {
                // The controller fault callback is the final managed diagnostic boundary.
                // Timer exceptions must never escape onto the WPF dispatcher.
            }
        }
    }

    private static void ValidateInterval(TimeSpan interval)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);
    }
}
