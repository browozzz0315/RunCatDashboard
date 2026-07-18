namespace RunCatDashboard.App.Windowing;

public interface IOverlayModeCoordinator
{
    OverlayWindowState State { get; }

    OverlayWindowState TrySetMode(OverlayInteractionMode mode);

    OverlayWindowState ToggleMode();
}
