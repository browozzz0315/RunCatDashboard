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
    event Action? ExitRequested;

    void Show();

    void SetMenuText(string visibilityText, string interactionText);

    void RecoverAfterExplorerRestart();
}
