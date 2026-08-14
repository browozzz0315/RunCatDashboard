namespace RunCatDashboard.App.Theming;

public interface IWindowsAppThemeDetector : IDisposable
{
    WindowsAppTheme Current { get; }

    event Action<WindowsAppTheme>? ThemeChanged;
}
