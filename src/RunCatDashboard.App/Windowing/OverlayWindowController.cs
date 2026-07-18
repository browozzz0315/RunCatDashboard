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
    private OverlayInteractionMode _requestedMode = OverlayInteractionMode.ClickThrough;
    private OverlayInteractionMode? _appliedMode;
    private bool _isFaulted;
    private string? _lastError;

    internal OverlayWindowController(INativeWindowStyleApi nativeApi)
    {
        ArgumentNullException.ThrowIfNull(nativeApi);
        _nativeApi = nativeApi;
    }

    public OverlayWindowState State => new(
        _requestedMode,
        _appliedMode,
        IsInitialized,
        _isFaulted,
        _lastError);

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

        ThrowIfFaulted();

        ApplyMode(windowHandle, _requestedMode, "initialize overlay window styles");
        _windowHandle = windowHandle;
        _appliedMode = _requestedMode;
        _lastError = null;
    }

    public bool SetMode(OverlayInteractionMode mode)
    {
        ObjectDisposedException.ThrowIf(_isClosed, this);
        ThrowIfFaulted();

        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown overlay interaction mode.");
        }

        if (_windowHandle == nint.Zero)
        {
            throw new InvalidOperationException("The native window handle has not been initialized.");
        }

        _requestedMode = mode;

        if (_appliedMode == mode)
        {
            _lastError = null;
            return false;
        }

        ApplyMode(_windowHandle, mode, $"switch overlay mode to {mode}");
        _appliedMode = mode;
        _lastError = null;
        return true;
    }

    public void Close()
    {
        _windowHandle = nint.Zero;
        _appliedMode = null;
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
            ThrowOperationFailure(new OverlayWindowException(
                $"Failed to {operation} because the current native style could not be read. " +
                DescribeNativeError(exception),
                exception));
            return;
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
            ThrowOperationFailure(new OverlayWindowException(
                $"Failed to {operation}; the style update was not confirmed. " +
                DescribeNativeError(exception),
                exception));
            return;
        }

        try
        {
            _nativeApi.RefreshFrame(windowHandle);
        }
        catch (Win32Exception refreshException)
        {
            RestorePreviousStyleOrFault(
                windowHandle,
                currentStyle,
                operation,
                refreshException);

            ThrowOperationFailure(new OverlayWindowException(
                $"Failed to {operation}; the previous native style was restored. " +
                DescribeNativeError(refreshException),
                refreshException));
        }
    }

    private void RestorePreviousStyleOrFault(
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
            var exception = new OverlayWindowException(
                $"Failed to {operation}, and restoring the previous style also failed. " +
                "The native window style is unknown. " +
                $"Original {DescribeNativeError(originalException)} " +
                $"Rollback {DescribeNativeError(rollbackException)}",
                new AggregateException(originalException, rollbackException));

            _isFaulted = true;
            _appliedMode = null;
            _lastError = exception.Message;
            throw exception;
        }
    }

    private void ThrowIfFaulted()
    {
        if (_isFaulted)
        {
            throw new OverlayWindowException(
                "The overlay window controller is faulted because its native style is unknown.",
                new InvalidOperationException(_lastError));
        }
    }

    private void ThrowOperationFailure(OverlayWindowException exception)
    {
        _lastError = exception.Message;
        throw exception;
    }

    private static string DescribeNativeError(Win32Exception exception)
    {
        return $"Win32 error {exception.NativeErrorCode}: {exception.Message}";
    }
}
