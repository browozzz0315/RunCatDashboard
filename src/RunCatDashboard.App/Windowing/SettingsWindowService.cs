namespace RunCatDashboard.App.Windowing;

public interface ISettingsWindowService
{
    bool IsOpen { get; }
    void Open();
    void Close();
}

internal sealed class SettingsWindowService : ISettingsWindowService
{
    private readonly Func<ISettingsWindowHost> _factory;
    private ISettingsWindowHost? _window;

    internal SettingsWindowService(Func<ISettingsWindowHost> factory)
    {
        _factory = factory;
    }

    public bool IsOpen => _window is not null;

    public void Open()
    {
        if (_window is not null)
        {
            if (_window.IsMinimized)
                _window.Restore();
            _window.Activate();
            return;
        }

        _window = _factory();
        _window.Closed += OnClosed;
        _window.Show();
        _window.Activate();
    }

    public void Close() => _window?.Close();

    private void OnClosed(object? sender, EventArgs e)
    {
        if (_window is not null)
            _window.Closed -= OnClosed;
        _window = null;
    }
}

internal interface ISettingsWindowHost
{
    bool IsMinimized { get; }
    event EventHandler? Closed;
    void Show();
    void Activate();
    void Restore();
    void Close();
}
