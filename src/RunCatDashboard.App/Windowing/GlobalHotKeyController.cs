using System.ComponentModel;
using Microsoft.Extensions.Logging;
using RunCatDashboard.App.Diagnostics;
using RunCatDashboard.App.Interop;

namespace RunCatDashboard.App.Windowing;

internal sealed class GlobalHotKeyController : IGlobalHotKeyController
{
    internal const int WindowMessageHotKey = 0x0312;
    internal const int InteractionHotKeyIdentifier = 0x5243;
    internal const int VisibilityHotKeyIdentifier = 0x5244;
    internal const uint ModifierAlt = 0x0001;
    internal const uint ModifierControl = 0x0002;
    internal const uint ModifierShift = 0x0004;
    internal const uint ModifierWindows = 0x0008;
    internal const uint ModifierNoRepeat = 0x4000;
    internal const uint VirtualKeyR = 0x52;
    internal const uint VirtualKeyD = 0x44;
    internal const uint HotKeyModifiers =
        ModifierControl | ModifierAlt | ModifierShift | ModifierNoRepeat;
    internal const string InteractionGestureText = "Ctrl + Alt + Shift + R";
    internal const string VisibilityGestureText = "Ctrl + Alt + Shift + D";

    private readonly object _gate = new();
    private readonly INativeGlobalHotKeyApi _nativeApi;
    private readonly ILogger<GlobalHotKeyController> _logger;
    private readonly Registration _interactionRegistration;
    private readonly Registration _visibilityRegistration;
    private readonly bool _initialInteractionGestureWasInvalid;
    private readonly bool _initialVisibilityGestureWasInvalid;
    private nint _windowHandle;
    private bool _registrationAttempted;
    private bool _isDisposed;

    internal GlobalHotKeyController(
        INativeGlobalHotKeyApi nativeApi,
        ILogger<GlobalHotKeyController>? logger = null,
        OverlayHotKeyGesture? initialInteractionGesture = null,
        OverlayHotKeyGesture? initialVisibilityGesture = null)
    {
        ArgumentNullException.ThrowIfNull(nativeApi);
        _nativeApi = nativeApi;
        _logger = logger ??
            Microsoft.Extensions.Logging.Abstractions.NullLogger<GlobalHotKeyController>.Instance;

        OverlayHotKeyGesture requestedInteraction =
            initialInteractionGesture ?? OverlayHotKeyGesture.Default;
        _initialInteractionGestureWasInvalid = !requestedInteraction.TryValidate(out _);
        if (_initialInteractionGestureWasInvalid)
        {
            requestedInteraction = OverlayHotKeyGesture.Default;
        }

        OverlayHotKeyGesture requestedVisibility =
            initialVisibilityGesture ?? OverlayHotKeyGesture.DashboardVisibilityDefault;
        _initialVisibilityGestureWasInvalid = !requestedVisibility.TryValidate(out _);
        if (_initialVisibilityGestureWasInvalid)
        {
            requestedVisibility = OverlayHotKeyGesture.DashboardVisibilityDefault;
        }

        if (requestedInteraction == requestedVisibility)
        {
            requestedVisibility = OverlayHotKeyGesture.DashboardVisibilityDefault;
            if (requestedInteraction == requestedVisibility)
            {
                requestedInteraction = OverlayHotKeyGesture.Default;
            }
        }

        _interactionRegistration = new Registration(
            GlobalHotKeyAction.ToggleInteractionMode,
            InteractionHotKeyIdentifier,
            requestedInteraction);
        _visibilityRegistration = new Registration(
            GlobalHotKeyAction.ToggleDashboardVisibility,
            VisibilityHotKeyIdentifier,
            requestedVisibility);
    }

    public IReadOnlyList<GlobalHotKeyRegistrationState> Registrations
    {
        get
        {
            lock (_gate)
            {
                return SnapshotLocked();
            }
        }
    }

    public OverlayHotKeyGesture InteractionGesture
    {
        get
        {
            lock (_gate)
            {
                return _interactionRegistration.Gesture;
            }
        }
    }

    public OverlayHotKeyGesture VisibilityGesture
    {
        get
        {
            lock (_gate)
            {
                return _visibilityRegistration.Gesture;
            }
        }
    }

    public IReadOnlyList<GlobalHotKeyRegistrationState> RegisterAll(nint windowHandle)
    {
        if (windowHandle == nint.Zero)
        {
            throw new ArgumentException(
                "A valid native window handle is required.",
                nameof(windowHandle));
        }

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            if (_registrationAttempted)
            {
                return SnapshotLocked();
            }

            _registrationAttempted = true;
            _windowHandle = windowHandle;
            RegisterInitialGestureLocked(
                _visibilityRegistration,
                _interactionRegistration,
                OverlayHotKeyGesture.DashboardVisibilityDefault,
                _initialVisibilityGestureWasInvalid);
            RegisterInitialGestureLocked(
                _interactionRegistration,
                _visibilityRegistration,
                OverlayHotKeyGesture.Default,
                _initialInteractionGestureWasInvalid);
            return SnapshotLocked();
        }
    }

    public GlobalHotKeyApplyResult ApplyInteractionGesture(OverlayHotKeyGesture gesture)
        => ApplyGesture(_interactionRegistration, _visibilityRegistration, gesture);

    public GlobalHotKeyApplyResult ApplyVisibilityGesture(OverlayHotKeyGesture gesture)
        => ApplyGesture(_visibilityRegistration, _interactionRegistration, gesture);

    private GlobalHotKeyApplyResult ApplyGesture(
        Registration registration,
        Registration otherRegistration,
        OverlayHotKeyGesture gesture)
    {
        ArgumentNullException.ThrowIfNull(gesture);
        if (!gesture.TryValidate(out string? validationError))
        {
            throw new ArgumentException(validationError, nameof(gesture));
        }

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            if (!_registrationAttempted)
            {
                throw new InvalidOperationException("Global hotkeys have not been initialized.");
            }

            if (otherRegistration.Gesture == gesture)
            {
                return new GlobalHotKeyApplyResult(
                    false,
                    false,
                    true,
                    false,
                    OverlayHotKeyGesture.DuplicateGestureMessage);
            }

            if (registration.Gesture == gesture && registration.IsRegistered)
            {
                return new GlobalHotKeyApplyResult(true, true, true, false, null);
            }

            if (registration.Gesture == gesture)
            {
                bool recovered = TryRegisterLocked(registration, "RegisterHotKey");
                return recovered
                    ? new GlobalHotKeyApplyResult(true, false, true, false, null)
                    : new GlobalHotKeyApplyResult(
                        false,
                        false,
                        false,
                        true,
                        registration.Fault);
            }

            OverlayHotKeyGesture previous = registration.Gesture;
            if (registration.IsRegistered &&
                !TryUnregisterLocked(registration, "ReplaceHotKey"))
            {
                return new GlobalHotKeyApplyResult(
                    false,
                    false,
                    true,
                    false,
                    $"無法解除原快捷鍵 {previous.DisplayText}；設定未變更。");
            }

            registration.Gesture = gesture;
            if (TryRegisterLocked(registration, "ReplaceHotKey"))
            {
                LogReplacementSuccess(registration, previous, gesture);
                return new GlobalHotKeyApplyResult(true, false, true, false, null);
            }

            string newGestureFault =
                $"快捷鍵 {gesture.DisplayText} 無法註冊，可能已被其他程式使用。";
            registration.Gesture = previous;
            if (TryRegisterLocked(registration, "RollbackHotKey"))
            {
                return new GlobalHotKeyApplyResult(
                    false,
                    false,
                    true,
                    false,
                    $"{newGestureFault} 已恢復原快捷鍵 {previous.DisplayText}。");
            }

            Exception? rollbackException = registration.LastException;
            registration.Fault =
                $"{newGestureFault} 原快捷鍵 {previous.DisplayText} 也無法恢復；" +
                "已切換至可操作的安全狀態，請使用系統匣控制。";
            LogRollbackFailure(registration, gesture, previous, rollbackException);
            return new GlobalHotKeyApplyResult(
                false,
                false,
                false,
                true,
                registration.Fault);
        }
    }

    public bool TryGetAction(int message, nint parameter, out GlobalHotKeyAction action)
    {
        lock (_gate)
        {
            if (message == WindowMessageHotKey)
            {
                Registration? registration = RegistrationsLocked().FirstOrDefault(
                    candidate => candidate.IsRegistered &&
                        parameter == new nint(candidate.Identifier));
                if (registration is not null)
                {
                    action = registration.Action;
                    return true;
                }
            }

            action = default;
            return false;
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

            foreach (Registration registration in RegistrationsLocked().Where(
                         candidate => candidate.IsRegistered))
            {
                TryUnregisterLocked(registration, "UnregisterHotKey");
            }

            _isDisposed = true;
        }
    }

    private void RegisterInitialGestureLocked(
        Registration registration,
        Registration otherRegistration,
        OverlayHotKeyGesture fallback,
        bool initialGestureWasInvalid)
    {
        OverlayHotKeyGesture requested = registration.Gesture;
        if (initialGestureWasInvalid)
        {
            LogStartupFallback(registration, requested, fallback, "InvalidSavedGesture");
        }

        if (registration.Gesture != otherRegistration.Gesture &&
            TryRegisterLocked(registration, "RegisterHotKey"))
        {
            return;
        }

        if (requested == fallback || fallback == otherRegistration.Gesture)
        {
            return;
        }

        LogStartupFallback(registration, requested, fallback, "RegistrationFailed");
        registration.Gesture = fallback;
        if (TryRegisterLocked(registration, "StartupFallbackHotKey"))
        {
            registration.Fault =
                $"已保存的快捷鍵 {requested.DisplayText} 無法使用，" +
                $"目前改用預設 {fallback.DisplayText}。";
        }
    }

    private bool TryRegisterLocked(Registration registration, string operation)
    {
        try
        {
            _nativeApi.Register(
                _windowHandle,
                registration.Identifier,
                GetNativeModifiers(registration.Gesture),
                (uint)registration.Gesture.Key);
            registration.IsRegistered = true;
            registration.Fault = null;
            registration.NativeErrorCode = null;
            registration.LastException = null;
            LogRecoveryIfNeeded(operation, registration);
            return true;
        }
        catch (Exception exception)
        {
            registration.IsRegistered = false;
            registration.Fault = registration.Action switch
            {
                GlobalHotKeyAction.ToggleDashboardVisibility =>
                    "顯示／隱藏快捷鍵註冊失敗，可能已被其他程式使用。",
                _ => "互動模式快捷鍵註冊失敗，可能已被其他程式使用。"
            };
            SetFailureDetails(registration, exception);
            LogFirstFailure(operation, registration, exception);
            return false;
        }
    }

    private bool TryUnregisterLocked(Registration registration, string operation)
    {
        try
        {
            _nativeApi.Unregister(_windowHandle, registration.Identifier);
            registration.IsRegistered = false;
            registration.Fault = null;
            registration.NativeErrorCode = null;
            registration.LastException = null;
            LogRecoveryIfNeeded(operation, registration);
            return true;
        }
        catch (Exception exception)
        {
            registration.Fault =
                $"解除快捷鍵 {registration.Gesture.DisplayText} 失敗；程式結束前可能仍由系統保留。";
            SetFailureDetails(registration, exception);
            LogFirstFailure(operation, registration, exception);
            return false;
        }
    }

    private static uint GetNativeModifiers(OverlayHotKeyGesture gesture)
    {
        uint modifiers = ModifierNoRepeat;
        if (gesture.Control) modifiers |= ModifierControl;
        if (gesture.Alt) modifiers |= ModifierAlt;
        if (gesture.Shift) modifiers |= ModifierShift;
        if (gesture.Windows) modifiers |= ModifierWindows;
        return modifiers;
    }

    private IReadOnlyList<GlobalHotKeyRegistrationState> SnapshotLocked() =>
        Array.AsReadOnly(RegistrationsLocked()
            .Select(registration => registration.ToState())
            .ToArray());

    private IEnumerable<Registration> RegistrationsLocked()
    {
        yield return _interactionRegistration;
        yield return _visibilityRegistration;
    }

    private static void SetFailureDetails(Registration registration, Exception exception)
    {
        registration.NativeErrorCode = (exception as Win32Exception)?.NativeErrorCode;
        registration.LastException = exception;
    }

    private void LogFirstFailure(
        string operation,
        Registration registration,
        Exception exception)
    {
        if (registration.FaultEpisode.Observe(isFaulted: true) !=
            FaultEpisodeTransition.Failed)
        {
            return;
        }

        TryLog(() => _logger.LogError(
            exception,
            "Global hotkey operation failed. {Operation} {Subsystem} {HotKeyId} {RequestedState} {AppliedState} {FaultState} {NativeErrorCode} {HResult}",
            operation,
            "GlobalHotKey",
            registration.Identifier,
            registration.Gesture.DisplayText,
            registration.IsRegistered,
            "Faulted",
            registration.NativeErrorCode,
            exception.HResult));
    }

    private void LogRecoveryIfNeeded(string operation, Registration registration)
    {
        if (registration.FaultEpisode.Observe(isFaulted: false) !=
            FaultEpisodeTransition.Recovered)
        {
            return;
        }

        TryLog(() => _logger.LogInformation(
            "Global hotkey recovered. {Operation} {Subsystem} {HotKeyId} {AppliedState} {FaultState}",
            operation,
            "GlobalHotKey",
            registration.Identifier,
            registration.Gesture.DisplayText,
            "Recovered"));
    }

    private void LogStartupFallback(
        Registration registration,
        OverlayHotKeyGesture requested,
        OverlayHotKeyGesture fallback,
        string reason)
    {
        TryLog(() => _logger.LogWarning(
            "Global hotkey startup fallback requested. {Operation} {Subsystem} {HotKeyId} {RequestedState} {AppliedState} {FaultState}",
            "StartupFallbackHotKey",
            "GlobalHotKey",
            registration.Identifier,
            requested.DisplayText,
            fallback.DisplayText,
            reason));
    }

    private void LogReplacementSuccess(
        Registration registration,
        OverlayHotKeyGesture previous,
        OverlayHotKeyGesture applied)
    {
        TryLog(() => _logger.LogInformation(
            "Global hotkey replacement applied. {Operation} {Subsystem} {HotKeyId} {PreviousState} {RequestedState} {AppliedState} {FaultState}",
            "ReplaceHotKey",
            "GlobalHotKey",
            registration.Identifier,
            previous.DisplayText,
            applied.DisplayText,
            applied.DisplayText,
            "None"));
    }

    private void LogRollbackFailure(
        Registration registration,
        OverlayHotKeyGesture requested,
        OverlayHotKeyGesture rollback,
        Exception? exception)
    {
        TryLog(() => _logger.LogError(
            exception,
            "Global hotkey rollback failed. {Operation} {Subsystem} {HotKeyId} {RequestedState} {AppliedState} {FaultState} {NativeErrorCode} {HResult}",
            "RollbackHotKey",
            "GlobalHotKey",
            registration.Identifier,
            requested.DisplayText,
            rollback.DisplayText,
            "Faulted",
            registration.NativeErrorCode,
            exception?.HResult));
    }

    private static void TryLog(Action log)
    {
        try
        {
            log();
        }
        catch
        {
            // Logging must not alter hotkey requested/applied/fault state.
        }
    }

    private sealed class Registration(
        GlobalHotKeyAction action,
        int identifier,
        OverlayHotKeyGesture gesture)
    {
        internal GlobalHotKeyAction Action { get; } = action;
        internal int Identifier { get; } = identifier;
        internal OverlayHotKeyGesture Gesture { get; set; } = gesture;
        internal bool IsRegistered { get; set; }
        internal string? Fault { get; set; }
        internal int? NativeErrorCode { get; set; }
        internal Exception? LastException { get; set; }
        internal FaultEpisodeTracker FaultEpisode { get; } = new();

        internal GlobalHotKeyRegistrationState ToState() => new(
            Action,
            Identifier,
            Gesture.DisplayText,
            IsRegistered,
            Fault,
            NativeErrorCode);
    }
}
