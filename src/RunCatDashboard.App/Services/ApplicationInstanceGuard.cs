namespace RunCatDashboard.App.Services;

public interface IApplicationInstanceGuard : IDisposable
{
    bool HasOwnership { get; }

    bool TryAcquireOwnership();
}

internal sealed class ApplicationInstanceException : InvalidOperationException
{
    internal ApplicationInstanceException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

internal interface IApplicationInstanceMutex : IDisposable
{
    bool WaitOne(int millisecondsTimeout);

    void ReleaseMutex();
}

internal interface IApplicationInstanceMutexFactory
{
    IApplicationInstanceMutex Create(string name);
}

internal sealed class WindowsApplicationInstanceGuard : IApplicationInstanceGuard
{
    internal const string MutexName = @"Local\RunCatDashboard.SingleInstance";

    private readonly object _gate = new();
    private readonly IApplicationInstanceMutexFactory _mutexFactory;
    private readonly string _mutexName;
    private IApplicationInstanceMutex? _mutex;
    private ApplicationInstanceException? _initializationFailure;
    private bool _hasAttemptedOwnership;
    private bool _hasOwnership;
    private bool _isDisposed;

    internal WindowsApplicationInstanceGuard()
        : this(new WindowsApplicationInstanceMutexFactory(), MutexName)
    {
    }

    internal WindowsApplicationInstanceGuard(
        IApplicationInstanceMutexFactory mutexFactory,
        string mutexName)
    {
        ArgumentNullException.ThrowIfNull(mutexFactory);
        ArgumentException.ThrowIfNullOrWhiteSpace(mutexName);
        _mutexFactory = mutexFactory;
        _mutexName = mutexName;
    }

    public bool HasOwnership
    {
        get
        {
            lock (_gate)
            {
                return _hasOwnership;
            }
        }
    }

    public bool TryAcquireOwnership()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            if (_hasAttemptedOwnership)
            {
                if (_initializationFailure is not null)
                {
                    throw _initializationFailure;
                }

                return _hasOwnership;
            }

            _hasAttemptedOwnership = true;
            try
            {
                _mutex = _mutexFactory.Create(_mutexName);
                try
                {
                    _hasOwnership = _mutex.WaitOne(0);
                }
                catch (AbandonedMutexException)
                {
                    _hasOwnership = true;
                }

                return _hasOwnership;
            }
            catch (Exception exception)
            {
                try
                {
                    _mutex?.Dispose();
                }
                catch (Exception disposeException)
                {
                    exception = new AggregateException(exception, disposeException);
                }

                _mutex = null;
                _initializationFailure = new ApplicationInstanceException(
                    $"Failed to initialize the single-instance guard '{_mutexName}': {exception.Message}",
                    exception);
                throw _initializationFailure;
            }
        }
    }

    public void Dispose()
    {
        IApplicationInstanceMutex? mutex;
        bool mustReleaseOwnership;

        lock (_gate)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            mutex = _mutex;
            _mutex = null;
            mustReleaseOwnership = _hasOwnership;
            _hasOwnership = false;
        }

        if (mutex is null)
        {
            return;
        }

        List<Exception>? failures = null;
        if (mustReleaseOwnership)
        {
            try
            {
                mutex.ReleaseMutex();
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
            }
        }

        try
        {
            mutex.Dispose();
        }
        catch (Exception exception)
        {
            (failures ??= []).Add(exception);
        }

        if (failures is not null)
        {
            Exception innerException = failures.Count == 1
                ? failures[0]
                : new AggregateException(failures);
            throw new ApplicationInstanceException(
                $"Failed to release the single-instance guard '{_mutexName}': {innerException.Message}",
                innerException);
        }
    }
}

internal sealed class WindowsApplicationInstanceMutexFactory : IApplicationInstanceMutexFactory
{
    public IApplicationInstanceMutex Create(string name)
    {
        return new WindowsApplicationInstanceMutex(new Mutex(false, name));
    }
}

internal sealed class WindowsApplicationInstanceMutex : IApplicationInstanceMutex
{
    private readonly Mutex _mutex;

    internal WindowsApplicationInstanceMutex(Mutex mutex)
    {
        ArgumentNullException.ThrowIfNull(mutex);
        _mutex = mutex;
    }

    public bool WaitOne(int millisecondsTimeout) =>
        _mutex.WaitOne(millisecondsTimeout);

    public void ReleaseMutex() => _mutex.ReleaseMutex();

    public void Dispose() => _mutex.Dispose();
}
