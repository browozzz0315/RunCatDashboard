using Microsoft.Extensions.Logging;
using RunCatDashboard.App.Diagnostics;
using RunCatDashboard.App.Settings;

namespace RunCatDashboard.App.Windowing;

public sealed class ExplicitShutdownCoordinator
{
    private readonly IWindowVisibilityCoordinator _visibility;
    private readonly ISettingsService _settings;
    private readonly IApplicationLoggingRuntime _loggingRuntime;
    private readonly ILogger _logger;

    internal ExplicitShutdownCoordinator(
        IWindowVisibilityCoordinator visibility,
        ISettingsService settings,
        IApplicationLoggingRuntime? loggingRuntime = null,
        ILogger? logger = null)
    {
        _visibility = visibility;
        _settings = settings;
        _loggingRuntime = loggingRuntime ?? new NullApplicationLoggingRuntime();
        _logger = logger ??
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
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

        try
        {
            _logger.LogInformation(
                "Explicit shutdown began. {Operation} {Subsystem}",
                "BeginExit",
                "Shutdown");
        }
        catch
        {
            // Logging failure must not prevent explicit shutdown.
        }
        captureFinalPosition();
        await _settings.FlushAsync(cancellationToken);
        closeSettingsWindow();
        closeMainWindow();
        try
        {
            await _loggingRuntime.FlushAsync(TimeSpan.FromSeconds(2), cancellationToken);
        }
        catch (Exception)
        {
            // Logging failure must not prevent application shutdown.
        }
        shutdownApplication();
        return true;
    }
}
