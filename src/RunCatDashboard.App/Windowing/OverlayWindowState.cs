namespace RunCatDashboard.App.Windowing;

public sealed record OverlayWindowState(
    OverlayInteractionMode RequestedMode,
    OverlayInteractionMode? AppliedMode,
    bool IsInitialized,
    bool IsFaulted,
    string? LastError);
