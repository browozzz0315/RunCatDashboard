using Microsoft.Extensions.Logging;
using RunCatDashboard.App.Animation;
using RunCatDashboard.App.Diagnostics;

namespace RunCatDashboard.App.Windowing;

internal interface ITrayAnimationCoordinator : IDisposable
{
    bool IsAnimated { get; }

    string? LastError { get; }

    event Action<string?>? DiagnosticChanged;

    bool Initialize();

    bool ToggleMode();

    void RestoreCurrentModeIcon();
}

internal sealed class TrayAnimationCoordinator : ITrayAnimationCoordinator
{
    private readonly ITrayIconAdapter _adapter;
    private readonly IRunCatAnimationController _animationController;
    private readonly ILogger<TrayAnimationCoordinator> _logger;
    private readonly FaultEpisodeTracker _frameFaultEpisode = new();
    private bool _isInitialized;
    private bool _isDisposed;

    internal TrayAnimationCoordinator(
        ITrayIconAdapter adapter,
        IRunCatAnimationController animationController,
        ILogger<TrayAnimationCoordinator>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentNullException.ThrowIfNull(animationController);
        _adapter = adapter;
        _animationController = animationController;
        _logger = logger ??
            Microsoft.Extensions.Logging.Abstractions.NullLogger<TrayAnimationCoordinator>.Instance;
    }

    public bool IsAnimated { get; private set; } = true;

    public string? LastError { get; private set; }

    public event Action<string?>? DiagnosticChanged;

    public bool Initialize()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        if (_isInitialized)
        {
            return false;
        }

        _animationController.FrameChanged += OnFrameChanged;
        _isInitialized = true;
        ApplyCurrentModeIcon();
        return true;
    }

    public bool ToggleMode()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        IsAnimated = !IsAnimated;
        ApplyCurrentModeIcon();
        return IsAnimated;
    }

    public void RestoreCurrentModeIcon()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        ApplyCurrentModeIcon();
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        if (_isInitialized)
        {
            _animationController.FrameChanged -= OnFrameChanged;
            _isInitialized = false;
        }

        DiagnosticChanged = null;
    }

    private void OnFrameChanged(int frameIndex)
    {
        if (!_isInitialized || _isDisposed || !IsAnimated)
        {
            return;
        }

        TrySetAnimatedFrame(frameIndex);
    }

    private void ApplyCurrentModeIcon()
    {
        if (!IsAnimated)
        {
            TrySetStaticIcon();
            return;
        }

        if (_adapter.CanUseAnimatedIcons)
        {
            TrySetAnimatedFrame(_animationController.FrameIndex);
            return;
        }

        try
        {
            _adapter.SetStaticIcon();
            TryLogWarning("LoadTrayAnimationIcons", "StaticFallback");
            SetDiagnostic("系統匣動畫圖示無法使用，已切換為靜態圖示。");
        }
        catch (Exception exception)
        {
            LogException("ApplyTrayStaticFallback", exception);
            SetDiagnostic("系統匣圖示無法使用，請重新啟動程式後再試。");
        }
    }

    private void TrySetAnimatedFrame(int frameIndex)
    {
        try
        {
            _adapter.SetAnimatedFrame(frameIndex);
            if (_frameFaultEpisode.Observe(isFaulted: false) == FaultEpisodeTransition.Recovered)
            {
                TryLogRecovery("AssignTrayAnimationFrame");
            }
            SetDiagnostic(null);
        }
        catch (Exception exception)
        {
            if (_frameFaultEpisode.Observe(isFaulted: true) == FaultEpisodeTransition.Failed)
            {
                LogException("AssignTrayAnimationFrame", exception);
            }
            SetDiagnostic("系統匣動畫暫時無法更新，已保留上一個有效圖示。");
        }
    }

    private void TrySetStaticIcon()
    {
        try
        {
            _adapter.SetStaticIcon();
            SetDiagnostic(null);
        }
        catch (Exception exception)
        {
            LogException("AssignTrayStaticIcon", exception);
            SetDiagnostic("系統匣圖示暫時無法切換，已保留上一個有效圖示。");
        }
    }

    private void SetDiagnostic(string? message)
    {
        if (LastError == message)
        {
            return;
        }

        LastError = message;
        DiagnosticChanged?.Invoke(message);
    }

    private void TryLogWarning(string operation, string appliedState)
    {
        try
        {
            _logger.LogWarning(
                "Tray animation resource fallback was applied. {Operation} {Subsystem} {RequestedState} {AppliedState} {FaultState}",
                operation,
                "TrayAnimation",
                "Animated",
                appliedState,
                "Faulted");
        }
        catch
        {
        }
    }

    private void LogException(string operation, Exception exception)
    {
        try
        {
            _logger.LogError(
                exception,
                "Tray animation operation failed. {Operation} {Subsystem} {RequestedState} {AppliedState} {FaultState} {HResult}",
                operation,
                "TrayAnimation",
                IsAnimated ? "Animated" : "Static",
                "PreviousIcon",
                "Faulted",
                exception.HResult);
        }
        catch
        {
            // Logging must not alter tray animation state.
        }
    }

    private void TryLogRecovery(string operation)
    {
        try
        {
            _logger.LogInformation(
                "Tray animation recovered. {Operation} {Subsystem} {FaultState}",
                operation,
                "TrayAnimation",
                "Recovered");
        }
        catch
        {
            // Logging must not alter tray animation state.
        }
    }
}
