using RunCatDashboard.App.Interop;

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
        ITrayAnimationCoordinator animationCoordinator)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentNullException.ThrowIfNull(messageApi);
        ArgumentNullException.ThrowIfNull(visibilityCoordinator);
        ArgumentNullException.ThrowIfNull(interactionToggleAction);
        ArgumentNullException.ThrowIfNull(exitCoordinator);
        ArgumentNullException.ThrowIfNull(animationCoordinator);
        _adapter = adapter;
        _messageApi = messageApi;
        _visibilityCoordinator = visibilityCoordinator;
        _interactionToggleAction = interactionToggleAction;
        _exitCoordinator = exitCoordinator;
        _animationCoordinator = animationCoordinator;
    }

    public string? LastError { get; private set; }

    public event Action<string?>? DiagnosticChanged;

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
        _adapter.ExitRequested += OnExitRequested;
        _visibilityCoordinator.StateChanged += OnVisibilityChanged;
        _interactionToggleAction.StateChanged += OnInteractionStateChanged;
        _animationCoordinator.DiagnosticChanged += OnAnimationDiagnosticChanged;

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
            SetServiceError($"系統匣初始化失敗：{exception.Message}");
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
            SetServiceError($"Explorer 重啟後恢復系統匣圖示失敗：{exception.Message}");
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
            SetServiceError($"釋放系統匣圖示失敗：{exception.Message}");
        }

        DiagnosticChanged = null;
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
            SetServiceError($"更新系統匣動畫選單狀態失敗：{exception.Message}");
        }
    }

    private void OnExitRequested() => _exitCoordinator.RequestExit();

    private void OnVisibilityChanged(WindowVisibilityState state)
    {
        try
        {
            RefreshMenu();
        }
        catch (Exception exception)
        {
            SetServiceError($"更新系統匣選單狀態失敗：{exception.Message}");
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
            SetServiceError($"更新系統匣互動模式狀態失敗：{exception.Message}");
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
        DiagnosticChanged?.Invoke(message);
    }

    private void DetachEvents()
    {
        _adapter.DoubleClicked -= OnVisibilityToggleRequested;
        _adapter.VisibilityToggleRequested -= OnVisibilityToggleRequested;
        _adapter.InteractionToggleRequested -= OnInteractionToggleRequested;
        _adapter.AnimationToggleRequested -= OnAnimationToggleRequested;
        _adapter.ExitRequested -= OnExitRequested;
        _visibilityCoordinator.StateChanged -= OnVisibilityChanged;
        _interactionToggleAction.StateChanged -= OnInteractionStateChanged;
        _animationCoordinator.DiagnosticChanged -= OnAnimationDiagnosticChanged;
    }
}
