using System.ComponentModel;
using Microsoft.Extensions.Logging;
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
    private readonly ILogger<OverlayWindowController> _logger;
    private nint _windowHandle;
    private bool _isClosed;
    private OverlayInteractionMode _requestedMode = OverlayInteractionMode.ClickThrough;
    private OverlayInteractionMode? _appliedMode;
    private bool _isFaulted;
    private string? _lastError;

    internal OverlayWindowController(
        INativeWindowStyleApi nativeApi,
        ILogger<OverlayWindowController>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(nativeApi);
        _nativeApi = nativeApi;
        _logger = logger ??
            Microsoft.Extensions.Logging.Abstractions.NullLogger<OverlayWindowController>.Instance;
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

        _requestedMode = mode;

        if (_windowHandle == nint.Zero)
        {
            _lastError = null;
            return false;
        }

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
            LogNativeFailure(operation, exception, mode, _appliedMode, _isFaulted);
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
            LogNativeFailure(operation, exception, mode, _appliedMode, _isFaulted);
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
            LogNativeFailure(operation, refreshException, mode, _appliedMode, _isFaulted);
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
            LogNativeFailure(
                $"{operation}:RollbackNativeStyle",
                rollbackException,
                _requestedMode,
                null,
                isFaulted: true);
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

    private void LogNativeFailure(
        string operation,
        Win32Exception exception,
        OverlayInteractionMode requestedMode,
        OverlayInteractionMode? appliedMode,
        bool isFaulted)
    {
        try
        {
            _logger.LogError(
                exception,
                "Overlay native operation failed. {Operation} {Subsystem} {NativeErrorCode} {HResult} {RequestedState} {AppliedState} {FaultState}",
                operation,
                "OverlayWindow",
                exception.NativeErrorCode,
                exception.HResult,
                requestedMode,
                appliedMode,
                isFaulted ? "Faulted" : "Recoverable");
        }
        catch
        {
            // Logging must not alter native requested/applied/fault state.
        }
    }
}
