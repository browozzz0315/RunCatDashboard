namespace RunCatDashboard.App.Windowing;

public interface IWindowVisibilityCoordinator : IDisposable
{
    WindowVisibilityState State { get; }

    event Action<WindowVisibilityState>? StateChanged;

    bool SetUserRequestedVisibility(bool isVisible);

    bool ToggleUserRequestedVisibility();

    bool SetFullscreenPolicyVisibility(bool isVisible);

    bool HandleWindowClosing();

    bool BeginExit();
}
