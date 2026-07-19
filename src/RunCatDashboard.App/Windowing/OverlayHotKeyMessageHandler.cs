namespace RunCatDashboard.App.Windowing;

internal sealed class OverlayHotKeyMessageHandler : IOverlayHotKeyMessageHandler
{
    private readonly IGlobalHotKeyController _hotKeyController;
    private readonly IInteractionModeToggleAction _interactionToggleAction;
    private readonly IWindowVisibilityCoordinator _visibilityCoordinator;

    internal OverlayHotKeyMessageHandler(
        IGlobalHotKeyController hotKeyController,
        IInteractionModeToggleAction interactionToggleAction,
        IWindowVisibilityCoordinator visibilityCoordinator)
    {
        ArgumentNullException.ThrowIfNull(hotKeyController);
        ArgumentNullException.ThrowIfNull(interactionToggleAction);
        ArgumentNullException.ThrowIfNull(visibilityCoordinator);
        _hotKeyController = hotKeyController;
        _interactionToggleAction = interactionToggleAction;
        _visibilityCoordinator = visibilityCoordinator;
    }

    public bool TryHandleMessage(int message, nint parameter)
    {
        if (!_hotKeyController.TryGetAction(message, parameter, out GlobalHotKeyAction action))
        {
            return false;
        }

        if (action == GlobalHotKeyAction.ToggleInteractionMode)
        {
            _interactionToggleAction.RequestToggle();
        }
        else
        {
            _visibilityCoordinator.ToggleUserRequestedVisibility();
        }

        return true;
    }
}
