namespace RunCatDashboard.App.Settings;

public interface ISettingsService : IAsyncDisposable
{
    AppSettings Current { get; }
    string? LastDiagnostic { get; }
    event Action<AppSettings>? Changed;
    event Action<string?>? DiagnosticChanged;
    Task LoadAsync(CancellationToken cancellationToken = default);
    bool Update(Func<AppSettings, AppSettings> update);
    Task FlushAsync(CancellationToken cancellationToken = default);
}

internal sealed class SettingsService : ISettingsService
{
    internal static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(500);
    private readonly ISettingsStore _store;
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private AppSettings _current = AppSettings.Defaults;
    private CancellationTokenSource? _debounceSource;
    private long _revision;
    private long _savedRevision;
    private bool _isDisposed;

    internal SettingsService(
        ISettingsStore store,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
        _delayAsync = delayAsync ?? Task.Delay;
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
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                SetDiagnostic($"保存設定失敗：{exception.Message}");
            }
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private void SetDiagnostic(string? diagnostic)
    {
        if (LastDiagnostic == diagnostic) return;
        LastDiagnostic = diagnostic;
        DiagnosticChanged?.Invoke(diagnostic);
    }
}
