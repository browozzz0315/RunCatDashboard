using System.ComponentModel;
using RunCatDashboard.App.Interop;

namespace RunCatDashboard.App.Windowing;

internal sealed class OverlayWindowException : InvalidOperationException
{
    internal OverlayWindowException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

internal sealed class OverlayWindowController : IOverlayWindowController
{
    private const ExtendedWindowStyle PersistentStyles = ExtendedWindowStyle.ToolWindow;
    private const ExtendedWindowStyle ClickThroughStyles =
        ExtendedWindowStyle.Transparent | ExtendedWindowStyle.NoActivate;

    private readonly INativeWindowStyleApi _nativeApi;
    private nint _windowHandle;
    private bool _isClosed;

    internal OverlayWindowController(INativeWindowStyleApi nativeApi)
    {
        ArgumentNullException.ThrowIfNull(nativeApi);
        _nativeApi = nativeApi;
    }

    public OverlayInteractionMode Mode { get; private set; } =
        OverlayInteractionMode.Interactive;

    public bool IsInitialized => _windowHandle != nint.Zero && !_isClosed;

    public void Initialize(nint windowHandle)
    {
        ObjectDisposedException.ThrowIf(_isClosed, this);

        if (windowHandle == nint.Zero)
        {
            throw new ArgumentException("A valid native window handle is required.", nameof(windowHandle));
        }

        if (_windowHandle != nint.Zero)
        {
            throw new InvalidOperationException("The native window handle has already been initialized.");
        }

        ApplyMode(windowHandle, Mode, "initialize overlay window styles");
        _windowHandle = windowHandle;
    }

    public bool SetMode(OverlayInteractionMode mode)
    {
        ObjectDisposedException.ThrowIf(_isClosed, this);

        if (_windowHandle == nint.Zero)
        {
            throw new InvalidOperationException("The native window handle has not been initialized.");
        }

        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown overlay interaction mode.");
        }

        if (Mode == mode)
        {
            return false;
        }

        ApplyMode(_windowHandle, mode, $"switch overlay mode to {mode}");
        Mode = mode;
        return true;
    }

    public void Close()
    {
        _windowHandle = nint.Zero;
        _isClosed = true;
    }

    private void ApplyMode(
        nint windowHandle,
        OverlayInteractionMode mode,
        string operation)
    {
        long currentStyle;

        try
        {
            currentStyle = _nativeApi.GetExtendedStyle(windowHandle);
        }
        catch (Win32Exception exception)
        {
            throw new OverlayWindowException(
                $"Failed to {operation} because the current native style could not be read.",
                exception);
        }

        long desiredStyle = NativeWindowStyleBits.Add(currentStyle, PersistentStyles);
        desiredStyle = mode == OverlayInteractionMode.ClickThrough
            ? NativeWindowStyleBits.Add(desiredStyle, ClickThroughStyles)
            : NativeWindowStyleBits.Remove(desiredStyle, ClickThroughStyles);

        if (desiredStyle == currentStyle)
        {
            return;
        }

        try
        {
            _nativeApi.SetExtendedStyle(windowHandle, desiredStyle);
        }
        catch (Win32Exception exception)
        {
            throw new OverlayWindowException(
                $"Failed to {operation}; the style update was not confirmed.",
                exception);
        }

        try
        {
            _nativeApi.RefreshFrame(windowHandle);
        }
        catch (Win32Exception refreshException)
        {
            RestorePreviousStyleOrThrow(
                windowHandle,
                currentStyle,
                operation,
                refreshException);

            throw new OverlayWindowException(
                $"Failed to {operation}; the previous native style was restored.",
                refreshException);
        }
    }

    private void RestorePreviousStyleOrThrow(
        nint windowHandle,
        long previousStyle,
        string operation,
        Win32Exception originalException)
    {
        try
        {
            _nativeApi.SetExtendedStyle(windowHandle, previousStyle);
            _nativeApi.RefreshFrame(windowHandle);
        }
        catch (Win32Exception rollbackException)
        {
            throw new OverlayWindowException(
                $"Failed to {operation}, and restoring the previous style also failed. " +
                "The native window style is unknown.",
                new AggregateException(originalException, rollbackException));
        }
    }
}
