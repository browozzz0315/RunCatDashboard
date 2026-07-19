namespace RunCatDashboard.App.Windowing;

public interface IOverlayDisplayMonitor : IDisposable
{
    OverlayDisplayPolicyState State { get; }

    event Action<OverlayDisplayPolicyState>? StateChanged;

    bool Start(nint overlayWindowHandle);

    bool SetPolicy(OverlayDisplayPolicy policy);

    bool Reevaluate();

    bool NotifyDisplaySettingsChanged();

    bool NotifyOverlayMonitorChanged();

    bool ReportFault(string message);

    void Stop();
}

internal interface IForegroundWindowEventHook : IDisposable
{
    bool Start(Action callback, Action<string> faultCallback);

    void Stop();
}

internal interface IReconciliationTimer : IDisposable
{
    bool Start(
        TimeSpan interval,
        Action callback,
        Action<string> faultCallback);

    void Stop();
}

internal sealed class OverlayDisplayMonitor : IOverlayDisplayMonitor
{
    internal static readonly TimeSpan DefaultReconciliationInterval =
        TimeSpan.FromSeconds(1);

    private readonly object _gate = new();
    private readonly object _publicationGate = new();
    private readonly IFullscreenObservationSource _observationSource;
    private readonly IForegroundWindowEventHook _foregroundHook;
    private readonly IReconciliationTimer _timer;
    private readonly OverlayDisplayPolicyCoordinator _coordinator = new();
    private readonly TimeSpan _reconciliationInterval;
    private FullscreenObservation _lastObservation = FullscreenObservation.Pending;
    private OverlayDisplayPolicyState _state;
    private nint _overlayWindowHandle;
    private bool _isStarted;
    private bool _isDisposed;
    private long _generation;
    private long _nextEvaluationSequence;
    private long _latestEvaluationSequence;
    private string? _lifecycleFault;
    private string? _externalFault;

    internal OverlayDisplayMonitor(
        IFullscreenObservationSource observationSource,
        IForegroundWindowEventHook foregroundHook,
        IReconciliationTimer timer,
        TimeSpan? reconciliationInterval = null)
    {
        ArgumentNullException.ThrowIfNull(observationSource);
        ArgumentNullException.ThrowIfNull(foregroundHook);
        ArgumentNullException.ThrowIfNull(timer);

        _reconciliationInterval =
            reconciliationInterval ?? DefaultReconciliationInterval;
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
            _reconciliationInterval,
            TimeSpan.Zero);

        _observationSource = observationSource;
        _foregroundHook = foregroundHook;
        _timer = timer;
        _state = _coordinator.State;
    }

    public event Action<OverlayDisplayPolicyState>? StateChanged;

    public OverlayDisplayPolicyState State
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    public bool Start(nint overlayWindowHandle)
    {
        if (overlayWindowHandle == nint.Zero)
        {
            throw new ArgumentException(
                "A valid overlay HWND is required.",
                nameof(overlayWindowHandle));
        }

        long generation;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            if (_isStarted)
            {
                return false;
            }

            _isStarted = true;
            _overlayWindowHandle = overlayWindowHandle;
            _lifecycleFault = null;
            _externalFault = null;
            generation = ++_generation;
        }

        TryStartLifecycleComponents(generation);
        EvaluateAndPublish(generation, clearExternalFault: true);
        return true;
    }

    public bool SetPolicy(OverlayDisplayPolicy policy)
    {
        OverlayDisplayPolicyState next;
        long generation;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            OverlayDisplayPolicy previous = _coordinator.RequestedPolicy;
            _coordinator.SetPolicy(policy);
            if (previous == policy)
            {
                return false;
            }

            generation = _generation;
            next = BuildStateLocked(_lastObservation);
        }

        PublishIfCurrent(generation, next);
        return true;
    }

    public bool Reevaluate()
    {
        long generation;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            if (!_isStarted)
            {
                return false;
            }

            generation = _generation;
        }

        EvaluateAndPublish(generation, clearExternalFault: true);
        return true;
    }

    public bool NotifyDisplaySettingsChanged() => Reevaluate();

    public bool NotifyOverlayMonitorChanged() => Reevaluate();

    public bool ReportFault(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        OverlayDisplayPolicyState next;
        long generation;
        lock (_gate)
        {
            if (_isDisposed)
            {
                return false;
            }

            if (_externalFault == message)
            {
                return false;
            }

            _externalFault = message;
            generation = _generation;
            next = BuildStateLocked(_lastObservation);
        }

        PublishIfCurrent(generation, next);
        return true;
    }

    public void Stop()
    {
        lock (_publicationGate)
        {
            lock (_gate)
            {
                if (!_isStarted)
                {
                    return;
                }

                _isStarted = false;
                _overlayWindowHandle = nint.Zero;
                _generation++;
            }
        }

        List<string>? failures = null;
        try
        {
            _foregroundHook.Stop();
        }
        catch (Exception exception)
        {
            (failures ??= []).Add($"Stopping the foreground hook failed: {exception.Message}");
        }

        try
        {
            _timer.Stop();
        }
        catch (Exception exception)
        {
            (failures ??= []).Add($"Stopping the reconciliation timer failed: {exception.Message}");
        }

        if (failures is not null)
        {
            lock (_gate)
            {
                _lifecycleFault = string.Join(" ", failures);
                _state = BuildStateLocked(_lastObservation);
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_isDisposed)
            {
                return;
            }
        }

        Stop();

        List<string>? failures = null;
        try
        {
            _foregroundHook.Dispose();
        }
        catch (Exception exception)
        {
            (failures ??= []).Add($"Disposing the foreground hook failed: {exception.Message}");
        }

        try
        {
            _timer.Dispose();
        }
        catch (Exception exception)
        {
            (failures ??= []).Add($"Disposing the reconciliation timer failed: {exception.Message}");
        }

        lock (_gate)
        {
            _isDisposed = true;
            if (failures is not null)
            {
                _lifecycleFault = string.Join(" ", failures);
                _state = BuildStateLocked(_lastObservation);
            }
        }
    }

    private void TryStartLifecycleComponents(long generation)
    {
        List<string>? failures = null;
        try
        {
            _foregroundHook.Start(
                () => EvaluateAndPublish(generation, clearExternalFault: true),
                message => RecordLifecycleCallbackFault(generation, message));
        }
        catch (Exception exception)
        {
            (failures ??= []).Add($"Starting the foreground hook failed: {exception.Message}");
        }

        try
        {
            _timer.Start(
                _reconciliationInterval,
                () => EvaluateAndPublish(generation, clearExternalFault: true),
                message => RecordLifecycleCallbackFault(generation, message));
        }
        catch (Exception exception)
        {
            (failures ??= []).Add($"Starting the reconciliation timer failed: {exception.Message}");
        }

        if (failures is not null)
        {
            lock (_gate)
            {
                if (_isStarted && generation == _generation)
                {
                    _lifecycleFault = string.Join(" ", failures);
                }
            }
        }
    }

    private void EvaluateAndPublish(long generation, bool clearExternalFault)
    {
        nint overlayWindowHandle;
        long evaluationSequence;
        lock (_gate)
        {
            if (!_isStarted || generation != _generation)
            {
                return;
            }

            overlayWindowHandle = _overlayWindowHandle;
            evaluationSequence = ++_nextEvaluationSequence;
            _latestEvaluationSequence = evaluationSequence;
        }

        FullscreenObservation observation;
        try
        {
            observation = _observationSource.Observe(overlayWindowHandle);
        }
        catch (Exception exception)
        {
            observation = new FullscreenObservation(
                false,
                false,
                "Foreground observation failed",
                "Overlay monitor observation failed",
                $"Fullscreen observation threw unexpectedly: {exception.Message}");
        }

        OverlayDisplayPolicyState next;
        lock (_gate)
        {
            if (!_isStarted ||
                generation != _generation ||
                evaluationSequence != _latestEvaluationSequence)
            {
                return;
            }

            if (clearExternalFault)
            {
                _externalFault = null;
            }

            _lastObservation = observation;
            next = BuildStateLocked(observation);
        }

        PublishIfCurrent(generation, next, evaluationSequence);
    }

    private void RecordLifecycleCallbackFault(long generation, string message)
    {
        OverlayDisplayPolicyState next;
        lock (_gate)
        {
            if (!_isStarted || generation != _generation)
            {
                return;
            }

            _externalFault = message;
            next = BuildStateLocked(_lastObservation);
        }

        PublishIfCurrent(generation, next);
    }

    private OverlayDisplayPolicyState BuildStateLocked(FullscreenObservation observation)
    {
        string? combinedFault = CombineFaults(
            observation.Fault,
            _lifecycleFault,
            _externalFault);
        FullscreenObservation effectiveObservation = observation with
        {
            Fault = combinedFault
        };
        return _coordinator.UpdateObservation(effectiveObservation);
    }

    private void PublishIfCurrent(
        long generation,
        OverlayDisplayPolicyState next,
        long? evaluationSequence = null)
    {
        lock (_publicationGate)
        {
            Action<OverlayDisplayPolicyState>? handler;
            lock (_gate)
            {
                if (!_isStarted ||
                    generation != _generation ||
                    (evaluationSequence.HasValue &&
                     evaluationSequence.Value != _latestEvaluationSequence))
                {
                    return;
                }

                OverlayDisplayPolicyState currentDesired =
                    BuildStateLocked(_lastObservation);
                if (next != currentDesired || next == _state)
                {
                    return;
                }

                _state = next;
                handler = StateChanged;
            }

            try
            {
                handler?.Invoke(next);
            }
            catch (Exception exception)
            {
                RecordSubscriberFault(generation, exception);
            }
        }
    }

    private void RecordSubscriberFault(long generation, Exception exception)
    {
        lock (_gate)
        {
            if (!_isStarted || generation != _generation)
            {
                return;
            }

            _externalFault =
                $"Publishing the display policy state failed: {exception.Message}";
            _state = BuildStateLocked(_lastObservation);
        }
    }

    private static string? CombineFaults(params string?[] faults)
    {
        string[] present = faults
            .Where(fault => !string.IsNullOrWhiteSpace(fault))
            .Distinct(StringComparer.Ordinal)
            .ToArray()!;
        return present.Length == 0 ? null : string.Join(" ", present);
    }
}
