namespace RunCatDashboard.App.Windowing;

internal sealed class WindowVisibilityCoordinator : IWindowVisibilityCoordinator
{
    private readonly object _gate = new();
    private WindowVisibilityState _state = new(true, true, true, false);
    private bool _isDisposed;

    public WindowVisibilityState State
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    public event Action<WindowVisibilityState>? StateChanged;

    public bool SetUserRequestedVisibility(bool isVisible) =>
        Update(state => state with { IsUserRequestedVisible = isVisible });

    public bool ToggleUserRequestedVisibility() =>
        Update(state => state with
        {
            IsUserRequestedVisible = !state.IsUserRequestedVisible
        });

    public bool SetFullscreenPolicyVisibility(bool isVisible) =>
        Update(state => state with { IsFullscreenPolicyVisible = isVisible });

    public bool HandleWindowClosing()
    {
        lock (_gate)
        {
            if (_state.IsExiting)
            {
                return false;
            }
        }

        SetUserRequestedVisibility(false);
        return true;
    }

    public bool BeginExit() =>
        Update(state => state with { IsExiting = true });

    public void Dispose()
    {
        lock (_gate)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            StateChanged = null;
        }
    }

    private bool Update(Func<WindowVisibilityState, WindowVisibilityState> update)
    {
        Action<WindowVisibilityState>? handler;
        WindowVisibilityState next;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            next = update(_state);
            next = next with
            {
                IsActuallyVisible =
                    next.IsUserRequestedVisible && next.IsFullscreenPolicyVisible
            };
            if (next == _state)
            {
                return false;
            }

            _state = next;
            handler = StateChanged;
        }

        handler?.Invoke(next);
        return true;
    }
}
