using RunCatDashboard.App.Models;

namespace RunCatDashboard.App.Services;

public interface ISystemMetricsService
{
    ValueTask<SystemMetricsSnapshot> SampleAsync(CancellationToken cancellationToken = default);
}
