using System.ComponentModel;
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
    internal const uint ModifierNoRepeat = 0x4000;
    internal const uint VirtualKeyR = 0x52;
    internal const uint VirtualKeyD = 0x44;
    internal const uint HotKeyModifiers =
        ModifierControl | ModifierAlt | ModifierShift | ModifierNoRepeat;
    internal const string InteractionGestureText = "Ctrl + Alt + Shift + R";
    internal const string VisibilityGestureText = "Ctrl + Alt + Shift + D";

    private readonly object _gate = new();
    private readonly INativeGlobalHotKeyApi _nativeApi;
    private readonly Dictionary<GlobalHotKeyAction, Registration> _registrations;
    private nint _windowHandle;
    private bool _registrationAttempted;
    private bool _isDisposed;

    internal GlobalHotKeyController(INativeGlobalHotKeyApi nativeApi)
    {
        ArgumentNullException.ThrowIfNull(nativeApi);
        _nativeApi = nativeApi;
        _registrations = new Dictionary<GlobalHotKeyAction, Registration>
        {
            [GlobalHotKeyAction.ToggleInteractionMode] = new(
                GlobalHotKeyAction.ToggleInteractionMode,
                InteractionHotKeyIdentifier,
                VirtualKeyR,
                InteractionGestureText),
            [GlobalHotKeyAction.ToggleDashboardVisibility] = new(
                GlobalHotKeyAction.ToggleDashboardVisibility,
                VisibilityHotKeyIdentifier,
                VirtualKeyD,
                VisibilityGestureText)
        };
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
            foreach (Registration registration in _registrations.Values)
            {
                TryRegisterLocked(registration);
            }

            return SnapshotLocked();
        }
    }

    public bool TryGetAction(
        int message,
        nint parameter,
        out GlobalHotKeyAction action)
    {
        lock (_gate)
        {
            if (message == WindowMessageHotKey)
            {
                Registration? registration = _registrations.Values.FirstOrDefault(
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

            foreach (Registration registration in _registrations.Values.Where(
                         candidate => candidate.IsRegistered))
            {
                TryUnregisterLocked(registration);
            }

            _isDisposed = true;
        }
    }

    private void TryRegisterLocked(Registration registration)
    {
        try
        {
            _nativeApi.Register(
                _windowHandle,
                registration.Identifier,
                HotKeyModifiers,
                registration.VirtualKey);
            registration.IsRegistered = true;
            registration.Fault = null;
            registration.NativeErrorCode = null;
        }
        catch (Exception exception)
        {
            registration.Fault = registration.Action switch
            {
                GlobalHotKeyAction.ToggleDashboardVisibility =>
                    "顯示／隱藏快捷鍵註冊失敗，可能已被其他程式使用。",
                _ => "互動模式快捷鍵註冊失敗，可能已被其他程式使用。"
            };
            registration.NativeErrorCode =
                (exception as Win32Exception)?.NativeErrorCode;
        }
    }

    private void TryUnregisterLocked(Registration registration)
    {
        try
        {
            _nativeApi.Unregister(_windowHandle, registration.Identifier);
            registration.IsRegistered = false;
            registration.Fault = null;
            registration.NativeErrorCode = null;
        }
        catch (Exception exception)
        {
            registration.Fault =
                $"解除快捷鍵 {registration.GestureText} 失敗；程式結束前可能仍由系統保留。";
            registration.NativeErrorCode =
                (exception as Win32Exception)?.NativeErrorCode;
        }
    }

    private IReadOnlyList<GlobalHotKeyRegistrationState> SnapshotLocked() =>
        Array.AsReadOnly(_registrations.Values
            .Select(registration => registration.ToState())
            .ToArray());

    private sealed class Registration(
        GlobalHotKeyAction action,
        int identifier,
        uint virtualKey,
        string gestureText)
    {
        internal GlobalHotKeyAction Action { get; } = action;
        internal int Identifier { get; } = identifier;
        internal uint VirtualKey { get; } = virtualKey;
        internal string GestureText { get; } = gestureText;
        internal bool IsRegistered { get; set; }
        internal string? Fault { get; set; }
        internal int? NativeErrorCode { get; set; }

        internal GlobalHotKeyRegistrationState ToState() => new(
            Action,
            Identifier,
            GestureText,
            IsRegistered,
            Fault,
            NativeErrorCode);
    }
}
