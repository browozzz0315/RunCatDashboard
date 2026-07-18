namespace RunCatDashboard.App.Windowing;

public interface IOverlayWindowController
{
    OverlayInteractionMode Mode { get; }

    bool IsInitialized { get; }

    void Initialize(nint windowHandle);

    bool SetMode(OverlayInteractionMode mode);

    void Close();
}
