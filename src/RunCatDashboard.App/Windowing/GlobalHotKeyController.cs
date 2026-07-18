using System.ComponentModel;
using RunCatDashboard.App.Interop;

namespace RunCatDashboard.App.Windowing;

internal sealed class GlobalHotKeyException : InvalidOperationException
{
    internal GlobalHotKeyException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

internal sealed class GlobalHotKeyController : IGlobalHotKeyController
{
    internal const int WindowMessageHotKey = 0x0312;
    internal const int HotKeyIdentifier = 0x5243;
    internal const uint ModifierAlt = 0x0001;
    internal const uint ModifierControl = 0x0002;
    internal const uint ModifierShift = 0x0004;
    internal const uint ModifierNoRepeat = 0x4000;
    internal const uint VirtualKeyR = 0x52;
    internal const uint HotKeyModifiers =
        ModifierControl | ModifierAlt | ModifierShift | ModifierNoRepeat;
    internal const string DefaultGestureText = "Ctrl + Alt + Shift + R";

    private readonly INativeGlobalHotKeyApi _nativeApi;
    private nint _windowHandle;

    internal GlobalHotKeyController(INativeGlobalHotKeyApi nativeApi)
    {
        ArgumentNullException.ThrowIfNull(nativeApi);
        _nativeApi = nativeApi;
    }

    public string GestureText => DefaultGestureText;

    public bool IsRegistered => _windowHandle != nint.Zero;

    public string? LastError { get; private set; }

    public bool Register(nint windowHandle)
    {
        if (windowHandle == nint.Zero)
        {
            throw new ArgumentException("A valid native window handle is required.", nameof(windowHandle));
        }

        if (IsRegistered)
        {
            return false;
        }

        try
        {
            _nativeApi.Register(
                windowHandle,
                HotKeyIdentifier,
                HotKeyModifiers,
                VirtualKeyR);
        }
        catch (Win32Exception exception)
        {
            var hotKeyException = new GlobalHotKeyException(
                $"Failed to register the global hotkey {GestureText}. " +
                "The key combination may already be in use. " +
                $"Win32 error {exception.NativeErrorCode}: {exception.Message}",
                exception);
            LastError = hotKeyException.Message;
            throw hotKeyException;
        }

        _windowHandle = windowHandle;
        LastError = null;
        return true;
    }

    public bool IsTargetMessage(int message, nint parameter)
    {
        return IsRegistered &&
            message == WindowMessageHotKey &&
            parameter == new nint(HotKeyIdentifier);
    }

    public bool Unregister()
    {
        if (!IsRegistered)
        {
            return false;
        }

        try
        {
            _nativeApi.Unregister(_windowHandle, HotKeyIdentifier);
        }
        catch (Win32Exception exception)
        {
            var hotKeyException = new GlobalHotKeyException(
                $"Failed to unregister the global hotkey {GestureText}. " +
                $"Win32 error {exception.NativeErrorCode}: {exception.Message}",
                exception);
            LastError = hotKeyException.Message;
            throw hotKeyException;
        }

        _windowHandle = nint.Zero;
        LastError = null;
        return true;
    }

    public void Close()
    {
        Unregister();
    }
}
