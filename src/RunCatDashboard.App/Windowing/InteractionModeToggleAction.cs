using RunCatDashboard.App.Services;

namespace RunCatDashboard.App.Windowing;

internal sealed class InteractionModeToggleAction : IInteractionModeToggleAction
{
    private readonly IUiDispatcher _dispatcher;
    private readonly IOverlayModeCoordinator _modeCoordinator;

    internal InteractionModeToggleAction(
        IUiDispatcher dispatcher,
        IOverlayModeCoordinator modeCoordinator)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(modeCoordinator);
        _dispatcher = dispatcher;
        _modeCoordinator = modeCoordinator;
    }

    public OverlayWindowState State => _modeCoordinator.State;

    public event Action<OverlayWindowState>? StateChanged;

    public void RequestToggle()
    {
        ValueTask dispatch = _dispatcher.InvokeAsync(ToggleAndPublish);
        if (!dispatch.IsCompletedSuccessfully)
        {
            _ = ObserveDispatchAsync(dispatch);
        }
    }

    public void RequestMode(OverlayInteractionMode mode)
    {
        ValueTask dispatch = _dispatcher.InvokeAsync(() => SetAndPublish(mode));
        if (!dispatch.IsCompletedSuccessfully)
        {
            _ = ObserveDispatchAsync(dispatch);
        }
    }

    private void ToggleAndPublish()
    {
        OverlayWindowState state = _modeCoordinator.ToggleMode();
        StateChanged?.Invoke(state);
    }

    private void SetAndPublish(OverlayInteractionMode mode)
    {
        OverlayWindowState state = _modeCoordinator.TrySetMode(mode);
        StateChanged?.Invoke(state);
    }

    private static async Task ObserveDispatchAsync(ValueTask dispatch)
    {
        try
        {
            await dispatch.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The coordinator retains native application faults. Dispatcher
            // shutdown must not let a tray callback terminate the process.
        }
    }
}
