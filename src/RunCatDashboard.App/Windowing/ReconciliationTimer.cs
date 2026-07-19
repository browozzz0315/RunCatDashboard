namespace RunCatDashboard.App.Windowing;

internal sealed class ReconciliationTimer : IReconciliationTimer
{
    private System.Threading.Timer? _timer;
    private Action? _callback;
    private Action<string>? _faultCallback;
    private bool _isDisposed;

    public bool Start(
        TimeSpan interval,
        Action callback,
        Action<string> faultCallback)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);
        ArgumentNullException.ThrowIfNull(callback);
        ArgumentNullException.ThrowIfNull(faultCallback);
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (_timer is not null)
        {
            return false;
        }

        _callback = callback;
        _faultCallback = faultCallback;
        _timer = new System.Threading.Timer(
            OnTick,
            null,
            interval,
            interval);
        return true;
    }

    public void Stop()
    {
        System.Threading.Timer? timer = Interlocked.Exchange(ref _timer, null);
        timer?.Dispose();
        _callback = null;
        _faultCallback = null;
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        Stop();
        _isDisposed = true;
    }

    private void OnTick(object? state)
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
                    $"Fullscreen reconciliation timer callback failed: {exception.Message}");
            }
            catch (Exception)
            {
                // Timer callbacks must not terminate the process. The supplied monitor
                // fault callback is the final managed diagnostic boundary.
            }
        }
    }
}
