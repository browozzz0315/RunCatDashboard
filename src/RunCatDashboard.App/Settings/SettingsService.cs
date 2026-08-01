using Microsoft.Extensions.Logging;
using RunCatDashboard.App.Diagnostics;

namespace RunCatDashboard.App.Settings;

public interface ISettingsService : IAsyncDisposable
{
    AppSettings Current { get; }
    string? LastDiagnostic { get; }
    event Action<AppSettings>? Changed;
    event Action<string?>? DiagnosticChanged;
    Task LoadAsync(CancellationToken cancellationToken = default);
    bool Update(Func<AppSettings, AppSettings> update);
    Task<bool> TryReplaceCurrentAsync(
        Func<AppSettings, AppSettings> replacement,
        CancellationToken cancellationToken = default);
    Task FlushAsync(CancellationToken cancellationToken = default);
}

internal sealed class SettingsService : ISettingsService
{
    internal static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(500);
    private readonly ISettingsStore _store;
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;
    private readonly ILogger<SettingsService> _logger;
    private readonly FaultEpisodeTracker _writeFaultEpisode = new();
    private readonly object _gate = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private AppSettings _current = AppSettings.Defaults;
    private CancellationTokenSource? _debounceSource;
    private long _revision;
    private long _savedRevision;
    private bool _isDisposed;

    internal SettingsService(
        ISettingsStore store,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null,
        ILogger<SettingsService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
        _delayAsync = delayAsync ?? Task.Delay;
        _logger = logger ??
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SettingsService>.Instance;
    }

    public AppSettings Current { get { lock (_gate) return _current; } }
    public string? LastDiagnostic { get; private set; }
    public event Action<AppSettings>? Changed;
    public event Action<string?>? DiagnosticChanged;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        SettingsLoadResult result = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            _current = result.Settings;
            _revision = 0;
            _savedRevision = 0;
        }
        SetDiagnostic(result.Diagnostic);
        if (result.Diagnostic is null)
        {
            TryLog(() => _logger.LogInformation(
                "Settings loaded. {Operation} {Subsystem} {SettingsVersion}",
                "LoadSettings",
                "Settings",
                result.Settings.Version));
        }
        else
        {
            TryLog(() => _logger.LogWarning(
                "Settings loaded with fallback diagnostic. {Operation} {Subsystem} {FaultState} {SettingsVersion}",
                "LoadSettings",
                "Settings",
                "Fallback",
                result.Settings.Version));
        }
    }

    public bool Update(Func<AppSettings, AppSettings> update)
    {
        ArgumentNullException.ThrowIfNull(update);
        AppSettings next;
        CancellationToken token;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            next = AppSettingsValidator.Normalize(update(_current));
            if (next == _current)
            {
                return false;
            }
            _current = next;
            _revision++;
            _debounceSource?.Cancel();
            _debounceSource?.Dispose();
            _debounceSource = new CancellationTokenSource();
            token = _debounceSource.Token;
        }
        Changed?.Invoke(next);
        _ = DebounceAsync(token);
        return true;
    }

    public async Task<bool> TryReplaceCurrentAsync(
        Func<AppSettings, AppSettings> replacement,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            while (true)
            {
                AppSettings candidate;
                long baseRevision;
                lock (_gate)
                {
                    ObjectDisposedException.ThrowIf(_isDisposed, this);
                    baseRevision = _revision;
                    candidate = AppSettingsValidator.Normalize(replacement(_current));
                }

                IPreparedSettingsWrite prepared;
                try
                {
                    prepared = await _store
                        .PrepareSaveAsync(candidate, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    ReportPersistenceFailure(
                        candidate,
                        exception,
                        "設定無法保存，設定未變更。");
                    return false;
                }

                using (prepared)
                {
                    CancellationTokenSource? debounce = null;
                    bool revisionChanged;
                    try
                    {
                        lock (_gate)
                        {
                            ObjectDisposedException.ThrowIf(_isDisposed, this);
                            revisionChanged = _revision != baseRevision;
                            if (!revisionChanged)
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                prepared.Commit();
                                debounce = _debounceSource;
                                _debounceSource = null;
                                _current = candidate;
                                _revision++;
                                _savedRevision = _revision;
                            }
                        }
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        ReportPersistenceFailure(
                            candidate,
                            exception,
                            "設定無法保存，設定未變更。");
                        return false;
                    }

                    if (revisionChanged)
                    {
                        continue;
                    }

                    debounce?.Cancel();
                    debounce?.Dispose();
                    SetDiagnostic(null);
                    ReportPersistenceRecovery(candidate);
                    Changed?.Invoke(candidate);
                    return true;
                }
            }
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        CancellationTokenSource? debounce;
        lock (_gate)
        {
            debounce = _debounceSource;
            _debounceSource = null;
        }
        debounce?.Cancel();
        debounce?.Dispose();
        await SaveLatestAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_isDisposed) return;
            _isDisposed = true;
        }
        await FlushAsync().ConfigureAwait(false);
        lock (_gate)
        {
            Changed = null;
            DiagnosticChanged = null;
        }
        _writeGate.Dispose();
    }

    private async Task DebounceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _delayAsync(DebounceDelay, cancellationToken).ConfigureAwait(false);
            await SaveLatestAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task SaveLatestAsync(CancellationToken cancellationToken)
    {
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            AppSettings snapshot;
            long revision;
            lock (_gate)
            {
                if (_savedRevision == _revision) return;
                snapshot = _current;
                revision = _revision;
            }
            try
            {
                await _store.SaveAsync(snapshot, cancellationToken).ConfigureAwait(false);
                lock (_gate) _savedRevision = Math.Max(_savedRevision, revision);
                SetDiagnostic(null);
                ReportPersistenceRecovery(snapshot);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                ReportPersistenceFailure(
                    snapshot,
                    exception,
                    "設定已在目前執行期間套用，但無法寫入設定檔，重新啟動後可能不會保留。");
            }
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private void ReportPersistenceFailure(
        AppSettings snapshot,
        Exception exception,
        string diagnostic)
    {
        if (_writeFaultEpisode.Observe(isFaulted: true) == FaultEpisodeTransition.Failed)
        {
            TryLog(() => _logger.LogError(
                exception,
                "Settings persistence failed. {Operation} {Subsystem} {FaultState} {SettingsVersion} {HResult}",
                "SaveSettings",
                "Settings",
                "Faulted",
                snapshot.Version,
                exception.HResult));
        }
        SetDiagnostic(diagnostic);
    }

    private void ReportPersistenceRecovery(AppSettings snapshot)
    {
        if (_writeFaultEpisode.Observe(isFaulted: false) == FaultEpisodeTransition.Recovered)
        {
            TryLog(() => _logger.LogInformation(
                "Settings persistence recovered. {Operation} {Subsystem} {FaultState} {SettingsVersion}",
                "SaveSettings",
                "Settings",
                "Recovered",
                snapshot.Version));
        }
    }

    private void SetDiagnostic(string? diagnostic)
    {
        if (LastDiagnostic == diagnostic) return;
        LastDiagnostic = diagnostic;
        DiagnosticChanged?.Invoke(diagnostic);
    }

    private static void TryLog(Action log)
    {
        try
        {
            log();
        }
        catch
        {
            // Logging must not alter settings persistence or diagnostic state.
        }
    }
}
