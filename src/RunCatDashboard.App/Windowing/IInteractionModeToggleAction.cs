namespace RunCatDashboard.App.Windowing;

public interface IInteractionModeToggleAction
{
    OverlayWindowState State { get; }

    event Action<OverlayWindowState>? StateChanged;

    void RequestToggle();

    void RequestMode(OverlayInteractionMode mode);
}
