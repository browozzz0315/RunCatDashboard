using System.ComponentModel;
using System.Runtime.InteropServices;
using RunCatDashboard.App.Interop;

namespace RunCatDashboard.App.Windowing;

internal sealed class Win32ForegroundWindowEventHook : IForegroundWindowEventHook
{
    private NativeFullscreenMethods.WinEventDelegate? _nativeCallback;
    private Action? _callback;
    private Action<string>? _faultCallback;
    private nint _hookHandle;
    private bool _isDisposed;

    public bool Start(Action callback, Action<string> faultCallback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        ArgumentNullException.ThrowIfNull(faultCallback);
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (_hookHandle != nint.Zero)
        {
            return false;
        }

        _callback = callback;
        _faultCallback = faultCallback;
        _nativeCallback = OnForegroundChanged;
        _hookHandle = NativeFullscreenMethods.SetWinEventHook(
            NativeFullscreenMethods.EventSystemForeground,
            NativeFullscreenMethods.EventSystemForeground,
            nint.Zero,
            _nativeCallback,
            0,
            0,
            NativeFullscreenMethods.WinEventOutOfContext |
            NativeFullscreenMethods.WinEventSkipOwnProcess);
        if (_hookHandle == nint.Zero)
        {
            int errorCode = Marshal.GetLastPInvokeError();
            ClearCallbacks();
            throw new Win32Exception(errorCode, "SetWinEventHook(EVENT_SYSTEM_FOREGROUND) failed.");
        }

        return true;
    }

    public void Stop()
    {
        if (_hookHandle == nint.Zero)
        {
            ClearCallbacks();
            return;
        }

        nint hookHandle = _hookHandle;
        _hookHandle = nint.Zero;
        if (!NativeFullscreenMethods.UnhookWinEvent(hookHandle))
        {
            int errorCode = Marshal.GetLastPInvokeError();
            ClearCallbacks();
            throw new Win32Exception(errorCode, "UnhookWinEvent failed.");
        }

        ClearCallbacks();
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        Stop();
        _isDisposed = true;
    }

    private void OnForegroundChanged(
        nint eventHook,
        uint eventType,
        nint windowHandle,
        int objectId,
        int childId,
        uint eventThread,
        uint eventTime)
    {
        try
        {
            _callback?.Invoke();
        }
        catch (Exception exception)
        {
            try
            {
                _faultCallback?.Invoke(
                    $"Foreground WinEvent callback failed: {exception.Message}");
            }
            catch (Exception)
            {
                // The monitor callback is the final managed fault boundary. It must not
                // allow an exception to cross the unmanaged WinEvent callback boundary.
            }
        }
    }

    private void ClearCallbacks()
    {
        _callback = null;
        _faultCallback = null;
        _nativeCallback = null;
    }
}
