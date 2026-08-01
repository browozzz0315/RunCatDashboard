namespace RunCatDashboard.App.Settings;

internal sealed record SettingsLoadResult(AppSettings Settings, string? Diagnostic);

internal interface IPreparedSettingsWrite : IDisposable
{
    void Commit();
}

internal interface ISettingsStore
{
    Task<SettingsLoadResult> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);

    Task<IPreparedSettingsWrite> PrepareSaveAsync(
        AppSettings settings,
        CancellationToken cancellationToken = default);
}
