namespace RunCatDashboard.App.Windowing;

public interface ISystemTrayService : IDisposable
{
    string? LastError { get; }

    event Action<string?>? DiagnosticChanged;

    bool Initialize();

    void RefreshMenu();

    bool TryHandleWindowMessage(int message);
}

internal interface ITrayIconAdapter : IDisposable
{
    event Action? DoubleClicked;
    event Action? VisibilityToggleRequested;
    event Action? InteractionToggleRequested;
    event Action? AnimationToggleRequested;
    event Action? ExitRequested;

    bool CanUseAnimatedIcons { get; }

    string? AnimationIconLoadError { get; }

    void Show();

    void SetMenuText(
        string visibilityText,
        string interactionText,
        string animationText);

    void SetAnimatedFrame(int frameIndex);

    void SetStaticIcon();

    void RecoverAfterExplorerRestart();
}
