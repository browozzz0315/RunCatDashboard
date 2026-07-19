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
    private int _taskbarCreatedMessage;
    private bool _isInitialized;
    private bool _isDisposed;

    internal SystemTrayService(
        ITrayIconAdapter adapter,
        IRegisteredWindowMessageApi messageApi,
        IWindowVisibilityCoordinator visibilityCoordinator,
        IInteractionModeToggleAction interactionToggleAction,
        IApplicationExitCoordinator exitCoordinator)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentNullException.ThrowIfNull(messageApi);
        ArgumentNullException.ThrowIfNull(visibilityCoordinator);
        ArgumentNullException.ThrowIfNull(interactionToggleAction);
        ArgumentNullException.ThrowIfNull(exitCoordinator);
        _adapter = adapter;
        _messageApi = messageApi;
        _visibilityCoordinator = visibilityCoordinator;
        _interactionToggleAction = interactionToggleAction;
        _exitCoordinator = exitCoordinator;
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
        _adapter.ExitRequested += OnExitRequested;
        _visibilityCoordinator.StateChanged += OnVisibilityChanged;
        _interactionToggleAction.StateChanged += OnInteractionStateChanged;

        try
        {
            _taskbarCreatedMessage = _messageApi.Register(TaskbarCreatedMessageName);
            RefreshMenu();
            _adapter.Show();
            _isInitialized = true;
            SetDiagnostic(null);
            return true;
        }
        catch (Exception exception)
        {
            SetDiagnostic($"系統匣初始化失敗：{exception.Message}");
            DetachEvents();
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
                : "切換為 Interactive");
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
            _adapter.RecoverAfterExplorerRestart();
            SetDiagnostic(null);
        }
        catch (Exception exception)
        {
            SetDiagnostic($"Explorer 重啟後恢復系統匣圖示失敗：{exception.Message}");
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
        try
        {
            _adapter.Dispose();
        }
        catch (Exception exception)
        {
            SetDiagnostic($"釋放系統匣圖示失敗：{exception.Message}");
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

    private void OnExitRequested() => _exitCoordinator.RequestExit();

    private void OnVisibilityChanged(WindowVisibilityState state)
    {
        try
        {
            RefreshMenu();
        }
        catch (Exception exception)
        {
            SetDiagnostic($"更新系統匣選單狀態失敗：{exception.Message}");
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
            SetDiagnostic($"更新系統匣互動模式狀態失敗：{exception.Message}");
        }
    }

    private void SetDiagnostic(string? message)
    {
        LastError = message;
        DiagnosticChanged?.Invoke(message);
    }

    private void DetachEvents()
    {
        _adapter.DoubleClicked -= OnVisibilityToggleRequested;
        _adapter.VisibilityToggleRequested -= OnVisibilityToggleRequested;
        _adapter.InteractionToggleRequested -= OnInteractionToggleRequested;
        _adapter.ExitRequested -= OnExitRequested;
        _visibilityCoordinator.StateChanged -= OnVisibilityChanged;
        _interactionToggleAction.StateChanged -= OnInteractionStateChanged;
    }
}
