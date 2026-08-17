using System.IO;
using Microsoft.Extensions.Logging;
using RunCatDashboard.App.Diagnostics;
using RunCatDashboard.App.Interop;
using RunCatDashboard.App.Services;

namespace RunCatDashboard.App.Windowing;

internal sealed class SystemTrayService : ISystemTrayService
{
    internal const string TaskbarCreatedMessageName = "TaskbarCreated";

    private readonly ITrayIconAdapter _adapter;
    private readonly IRegisteredWindowMessageApi _messageApi;
    private readonly IWindowVisibilityCoordinator _visibilityCoordinator;
    private readonly IInteractionModeToggleAction _interactionToggleAction;
    private readonly IApplicationExitCoordinator _exitCoordinator;
    private readonly ITrayAnimationCoordinator _animationCoordinator;
    private readonly IApplicationPaths _applicationPaths;
    private readonly IApplicationFolderOpener _folderOpener;
    private readonly ILogger<SystemTrayService> _logger;
    private readonly FaultEpisodeTracker _faultEpisode = new();
    private int _taskbarCreatedMessage;
    private string? _serviceError;
    private bool _isInitialized;
    private bool _isDisposed;

    internal SystemTrayService(
        ITrayIconAdapter adapter,
        IRegisteredWindowMessageApi messageApi,
        IWindowVisibilityCoordinator visibilityCoordinator,
        IInteractionModeToggleAction interactionToggleAction,
        IApplicationExitCoordinator exitCoordinator,
        ITrayAnimationCoordinator animationCoordinator,
        IApplicationPaths applicationPaths,
        IApplicationFolderOpener folderOpener,
        ILogger<SystemTrayService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentNullException.ThrowIfNull(messageApi);
        ArgumentNullException.ThrowIfNull(visibilityCoordinator);
        ArgumentNullException.ThrowIfNull(interactionToggleAction);
        ArgumentNullException.ThrowIfNull(exitCoordinator);
        ArgumentNullException.ThrowIfNull(animationCoordinator);
        ArgumentNullException.ThrowIfNull(applicationPaths);
        ArgumentNullException.ThrowIfNull(folderOpener);
        _adapter = adapter;
        _messageApi = messageApi;
        _visibilityCoordinator = visibilityCoordinator;
        _interactionToggleAction = interactionToggleAction;
        _exitCoordinator = exitCoordinator;
        _animationCoordinator = animationCoordinator;
        _applicationPaths = applicationPaths;
        _folderOpener = folderOpener;
        _logger = logger ??
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SystemTrayService>.Instance;
    }

    public string? LastError { get; private set; }

    public event Action<string?>? DiagnosticChanged;
    public event Action? SettingsRequested;

    public bool Initialize()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        if (_isInitialized)
        {
            return false;
        }

        _adapter.DoubleClicked += OnVisibilityToggleRequested;
        _adapter.VisibilityToggleRequested += OnVisibilityToggleRequested;
        _adapter.InteractionToggleRequested += OnInteractionToggleRequested;
        _adapter.AnimationToggleRequested += OnAnimationToggleRequested;
        _adapter.SettingsRequested += OnSettingsRequested;
        _adapter.ExitRequested += OnExitRequested;
        _visibilityCoordinator.StateChanged += OnVisibilityChanged;
        _interactionToggleAction.StateChanged += OnInteractionStateChanged;
        _animationCoordinator.DiagnosticChanged += OnAnimationDiagnosticChanged;
        _adapter.OpenLogsDirectoryRequested += OnOpenLogsDirectoryRequested;

        try
        {
            _taskbarCreatedMessage = _messageApi.Register(TaskbarCreatedMessageName);
            _animationCoordinator.Initialize();
            RefreshMenu();
            _adapter.Show();
            _isInitialized = true;
            SetServiceError(null);
            return true;
        }
        catch (Exception exception)
        {
            LogException("InitializeSystemTray", exception);
            SetServiceError("系統匣初始化失敗，請重新啟動程式後再試。");
            DetachEvents();
            _animationCoordinator.Dispose();
            return false;
        }
    }

    public void RefreshMenu()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        WindowVisibilityState visibility = _visibilityCoordinator.State;
        OverlayWindowState mode = _interactionToggleAction.State;
        OverlayInteractionMode currentMode = mode.AppliedMode ?? mode.RequestedMode;
        _adapter.SetMenuText(
            visibility.IsUserRequestedVisible ? "隱藏 Dashboard" : "顯示 Dashboard",
            currentMode == OverlayInteractionMode.Interactive
                ? "切換為 Click-through"
                : "切換為 Interactive",
            _animationCoordinator.IsAnimated
                ? "停用系統匣動畫（改用靜態圖示）"
                : "啟用系統匣動畫");
    }

    public bool TryHandleWindowMessage(int message)
    {
        if (!_isInitialized || message != _taskbarCreatedMessage)
        {
            return false;
        }

        try
        {
            RefreshMenu();
            _animationCoordinator.RestoreCurrentModeIcon();
            _adapter.RecoverAfterExplorerRestart();
            SetServiceError(null);
        }
        catch (Exception exception)
        {
            LogException("RecoverSystemTrayAfterExplorerRestart", exception);
            SetServiceError("Explorer 重啟後無法恢復系統匣圖示，請重新啟動程式。");
        }

        return true;
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        DetachEvents();
        _animationCoordinator.Dispose();
        try
        {
            _adapter.Dispose();
        }
        catch (Exception exception)
        {
            LogException("DisposeSystemTray", exception);
            SetServiceError("系統匣結束清理未完整完成。");
        }

        DiagnosticChanged = null;
        SettingsRequested = null;
    }

    private void OnVisibilityToggleRequested()
    {
        _visibilityCoordinator.ToggleUserRequestedVisibility();
    }

    private void OnInteractionToggleRequested()
    {
        _interactionToggleAction.RequestToggle();
    }

    private void OnAnimationToggleRequested()
    {
        _animationCoordinator.ToggleMode();
        try
        {
            RefreshMenu();
        }
        catch (Exception exception)
        {
            LogException("UpdateTrayAnimationMenu", exception);
            SetServiceError("系統匣選單暫時無法更新。");
        }
    }

    private void OnExitRequested() => _exitCoordinator.RequestExit();

    private void OnSettingsRequested() => SettingsRequested?.Invoke();

    private void OnOpenLogsDirectoryRequested()
    {
        try
        {
            Directory.CreateDirectory(_applicationPaths.LogsDirectory);
            _folderOpener.Open(_applicationPaths.LogsDirectory);
            SetServiceError(null);
        }
        catch (Exception exception)
        {
            LogException("OpenLogsDirectory", exception);
            SetServiceError("無法開啟記錄資料夾，請稍後再試。");
        }
    }

    private void OnVisibilityChanged(WindowVisibilityState state)
    {
        try
        {
            RefreshMenu();
        }
        catch (Exception exception)
        {
            LogException("UpdateTrayVisibilityMenu", exception);
            SetServiceError("系統匣選單暫時無法更新。");
        }
    }

    private void OnInteractionStateChanged(OverlayWindowState state)
    {
        try
        {
            RefreshMenu();
        }
        catch (Exception exception)
        {
            LogException("UpdateTrayInteractionMenu", exception);
            SetServiceError("系統匣選單暫時無法更新。");
        }
    }

    private void OnAnimationDiagnosticChanged(string? message)
    {
        PublishDiagnostic();
    }

    private void SetServiceError(string? message)
    {
        _serviceError = message;
        PublishDiagnostic();
    }

    private void PublishDiagnostic()
    {
        string? message = string.Join(
            " ",
            new[] { _serviceError, _animationCoordinator.LastError }
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal));
        if (message.Length == 0)
        {
            message = null;
        }

        if (LastError == message)
        {
            return;
        }

        LastError = message;
        FaultEpisodeTransition transition = _faultEpisode.Observe(message is not null);
        try
        {
            if (transition == FaultEpisodeTransition.Failed)
            {
                _logger.LogWarning(
                    "System tray entered a fault episode. {Operation} {Subsystem} {FaultState}",
                    "UpdateSystemTray",
                    "SystemTray",
                    "Faulted");
            }
            else if (transition == FaultEpisodeTransition.Recovered)
            {
                _logger.LogInformation(
                    "System tray recovered. {Operation} {Subsystem} {FaultState}",
                    "UpdateSystemTray",
                    "SystemTray",
                    "Recovered");
            }
        }
        catch
        {
            // Logging must not suppress the existing diagnostic publication.
        }
        DiagnosticChanged?.Invoke(message);
    }

    private void DetachEvents()
    {
        _adapter.DoubleClicked -= OnVisibilityToggleRequested;
        _adapter.VisibilityToggleRequested -= OnVisibilityToggleRequested;
        _adapter.InteractionToggleRequested -= OnInteractionToggleRequested;
        _adapter.AnimationToggleRequested -= OnAnimationToggleRequested;
        _adapter.SettingsRequested -= OnSettingsRequested;
        _adapter.ExitRequested -= OnExitRequested;
        _visibilityCoordinator.StateChanged -= OnVisibilityChanged;
        _interactionToggleAction.StateChanged -= OnInteractionStateChanged;
        _animationCoordinator.DiagnosticChanged -= OnAnimationDiagnosticChanged;
        _adapter.OpenLogsDirectoryRequested -= OnOpenLogsDirectoryRequested;
    }

    private void LogException(string operation, Exception exception)
    {
        try
        {
            _logger.LogError(
                exception,
                "System tray operation failed. {Operation} {Subsystem} {FaultState} {NativeErrorCode} {HResult}",
                operation,
                "SystemTray",
                "Faulted",
                (exception as System.ComponentModel.Win32Exception)?.NativeErrorCode,
                exception.HResult);
        }
        catch
        {
            // Logging must not alter tray diagnostic state.
        }
    }
}
