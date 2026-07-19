namespace RunCatDashboard.App.Windowing;

public interface IApplicationExitCoordinator : IDisposable
{
    bool IsExitRequested { get; }

    event Action? ExitRequested;

    bool RequestExit();
}

internal sealed class ApplicationExitCoordinator : IApplicationExitCoordinator
{
    private bool _isDisposed;

    public bool IsExitRequested { get; private set; }

    public event Action? ExitRequested;

    public bool RequestExit()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        if (IsExitRequested)
        {
            return false;
        }

        IsExitRequested = true;
        ExitRequested?.Invoke();
        return true;
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        ExitRequested = null;
    }
}
