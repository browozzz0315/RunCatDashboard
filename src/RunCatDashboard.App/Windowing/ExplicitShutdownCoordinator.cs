using RunCatDashboard.App.Settings;

namespace RunCatDashboard.App.Windowing;

public sealed class ExplicitShutdownCoordinator
{
    private readonly IWindowVisibilityCoordinator _visibility;
    private readonly ISettingsService _settings;

    internal ExplicitShutdownCoordinator(
        IWindowVisibilityCoordinator visibility,
        ISettingsService settings)
    {
        _visibility = visibility;
        _settings = settings;
    }

    internal async Task<bool> ShutdownAsync(
        Action captureFinalPosition,
        Action closeSettingsWindow,
        Action closeMainWindow,
        Action shutdownApplication,
        CancellationToken cancellationToken = default)
    {
        if (!_visibility.BeginExit())
        {
            return false;
        }

        captureFinalPosition();
        await _settings.FlushAsync(cancellationToken);
        closeSettingsWindow();
        closeMainWindow();
        shutdownApplication();
        return true;
    }
}
