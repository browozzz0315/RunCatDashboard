using RunCatDashboard.App.Settings;

namespace RunCatDashboard.App.Theming;

public interface IThemeCoordinator : IDisposable
{
    ThemePreference ThemePreference { get; }

    ResolvedTheme ResolvedTheme { get; }

    event Action<ResolvedTheme>? ResolvedThemeChanged;

    Task ApplyPreferenceAsync(
        ThemePreference preference,
        CancellationToken cancellationToken = default);
}
