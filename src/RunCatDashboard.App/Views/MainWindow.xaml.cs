using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Microsoft.Win32;
using RunCatDashboard.App.ViewModels;
using RunCatDashboard.App.Windowing;

namespace RunCatDashboard.App.Views;

public partial class MainWindow : Window
{
    private const double InitialWorkAreaMargin = 16d;

    private readonly MainWindowViewModel _viewModel;
    private readonly IOverlayWindowController _overlayWindowController;
    private readonly IInteractionModeToggleAction _interactionToggleAction;
    private readonly IGlobalHotKeyController _globalHotKeyController;
    private readonly IOverlayHotKeyMessageHandler _hotKeyMessageHandler;
    private readonly IWindowVisibilityCoordinator _visibilityCoordinator;
    private readonly ISystemTrayService _trayService;
    private readonly IApplicationExitCoordinator _exitCoordinator;
    private readonly IWindowWorkAreaProvider _workAreaProvider;
    private readonly IOverlayDisplayMonitor _displayMonitor;
    private HwndSource? _windowSource;
    private nint _windowHandle;
    private bool _isHookInstalled;
    private bool _isDisplaySettingsHandlerRegistered;
    private bool _isActuallyVisible = true;
    private bool _appliedPolicyTopmost = true;
    private bool _isClosed;

    public MainWindow(
        MainWindowViewModel viewModel,
        IOverlayWindowController overlayWindowController,
        IInteractionModeToggleAction interactionToggleAction,
        IGlobalHotKeyController globalHotKeyController,
        IOverlayHotKeyMessageHandler hotKeyMessageHandler,
        IWindowVisibilityCoordinator visibilityCoordinator,
        ISystemTrayService trayService,
        IApplicationExitCoordinator exitCoordinator,
        IWindowWorkAreaProvider workAreaProvider,
        IOverlayDisplayMonitor displayMonitor)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _overlayWindowController = overlayWindowController;
        _interactionToggleAction = interactionToggleAction;
        _globalHotKeyController = globalHotKeyController;
        _hotKeyMessageHandler = hotKeyMessageHandler;
        _visibilityCoordinator = visibilityCoordinator;
        _trayService = trayService;
        _exitCoordinator = exitCoordinator;
        _workAreaProvider = workAreaProvider;
        _displayMonitor = displayMonitor;
        DataContext = viewModel;

        Loaded += OnLoaded;
        Closing += OnWindowClosing;
        LocationChanged += OnOverlayLocationChanged;
        _viewModel.DisplayPolicyRequested += OnDisplayPolicyRequested;
        _displayMonitor.StateChanged += OnDisplayPolicyStateChanged;
        _visibilityCoordinator.StateChanged += OnVisibilityStateChanged;
        _trayService.DiagnosticChanged += OnTrayDiagnosticChanged;
        _exitCoordinator.ExitRequested += OnExitRequested;
        _interactionToggleAction.StateChanged += OnInteractionModeStateChanged;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        _windowHandle = new WindowInteropHelper(this).Handle;
        StartDisplayPolicyMonitoring();
        bool isHookInstalled = TryInstallMessageHook();
        if (isHookInstalled)
        {
            IReadOnlyList<GlobalHotKeyRegistrationState> registrations =
                _globalHotKeyController.RegisterAll(_windowHandle);
            _viewModel.ApplyHotKeyRegistrations(registrations);
        }

        try
        {
            _overlayWindowController.Initialize(_windowHandle);
            _viewModel.ApplyOverlayState(_interactionToggleAction.State);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or ArgumentException)
        {
            _viewModel.ApplyOverlayState(
                _interactionToggleAction.State,
                $"Overlay initialization failed: {exception.Message}");

        }

        _trayService.Initialize();
        _viewModel.ReportTrayError(_trayService.LastError);
        RegisterDisplaySettingsHandler();
        PositionAtInitialWorkAreaLocation();
    }

    protected override void OnClosed(EventArgs e)
    {
        _isClosed = true;
        _viewModel.SetAnimationVisibility(false);
        Loaded -= OnLoaded;
        Closing -= OnWindowClosing;
        LocationChanged -= OnOverlayLocationChanged;
        _viewModel.DisplayPolicyRequested -= OnDisplayPolicyRequested;
        _displayMonitor.StateChanged -= OnDisplayPolicyStateChanged;
        _visibilityCoordinator.StateChanged -= OnVisibilityStateChanged;
        _trayService.DiagnosticChanged -= OnTrayDiagnosticChanged;
        _exitCoordinator.ExitRequested -= OnExitRequested;
        _interactionToggleAction.StateChanged -= OnInteractionModeStateChanged;
        _globalHotKeyController.Dispose();
        _viewModel.ApplyHotKeyRegistrations(_globalHotKeyController.Registrations);
        RemoveMessageHook();
        _trayService.Dispose();
        _displayMonitor.Stop();
        UnregisterDisplaySettingsHandler();
        _overlayWindowController.Close();
        _windowHandle = nint.Zero;
        _viewModel.DisposeAsync().AsTask().GetAwaiter().GetResult();
        base.OnClosed(e);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        _viewModel.Start();
        _viewModel.SetAnimationVisibility(true);
    }

    private bool TryInstallMessageHook()
    {
        if (_isHookInstalled)
        {
            return true;
        }

        _windowSource = HwndSource.FromHwnd(_windowHandle);
        if (_windowSource is null)
        {
            _viewModel.ReportOverlayError(
                "Overlay initialization failed: the WPF HWND message source is unavailable.");
            return false;
        }

        try
        {
            _windowSource.AddHook(OnWindowMessage);
            _isHookInstalled = true;
            return true;
        }
        catch (Exception exception)
        {
            _windowSource = null;
            _viewModel.ReportOverlayError(
                $"Overlay initialization failed while installing the HWND message hook: {exception.Message}");
            return false;
        }
    }

    private void RemoveMessageHook()
    {
        if (!_isHookInstalled)
        {
            return;
        }

        try
        {
            _windowSource?.RemoveHook(OnWindowMessage);
        }
        catch (Exception exception)
        {
            _viewModel.ReportOverlayError(
                $"Removing the HWND message hook failed: {exception.Message}");
        }

        _windowSource = null;
        _isHookInstalled = false;
    }

    private nint OnWindowMessage(
        nint windowHandle,
        int message,
        nint wordParameter,
        nint longParameter,
        ref bool handled)
    {
        try
        {
            _hotKeyMessageHandler.TryHandleMessage(message, wordParameter);

            _trayService.TryHandleWindowMessage(message);
        }
        catch (Exception exception)
        {
            try
            {
                _viewModel.ReportOverlayError(
                    $"Global hotkey handling failed: {exception.Message}");
            }
            catch (Exception)
            {
            }
        }

        return nint.Zero;
    }

    private void OnDragSurfaceMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_viewModel.IsInteractive || e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        DragMove();
        TryClampToCurrentWorkArea();
    }

    private void OnCloseButtonClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_visibilityCoordinator.HandleWindowClosing())
        {
            e.Cancel = true;
        }
    }

    private void OnExitRequested()
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_isClosed)
            {
                return;
            }

            _visibilityCoordinator.BeginExit();
            Close();
        });
    }

    private void OnTrayDiagnosticChanged(string? message)
    {
        Dispatcher.BeginInvoke(() => _viewModel.ReportTrayError(message));
    }

    private void OnInteractionModeStateChanged(OverlayWindowState state)
    {
        _viewModel.ApplyOverlayState(state);
    }

    private void RegisterDisplaySettingsHandler()
    {
        if (_isDisplaySettingsHandlerRegistered)
        {
            return;
        }

        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        _isDisplaySettingsHandlerRegistered = true;
    }

    private void UnregisterDisplaySettingsHandler()
    {
        if (!_isDisplaySettingsHandlerRegistered)
        {
            return;
        }

        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _isDisplaySettingsHandlerRegistered = false;
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        if (_isClosed)
        {
            return;
        }

        try
        {
            Dispatcher.Invoke(() =>
            {
                if (!_isClosed)
                {
                    TryClampToCurrentWorkArea();
                    _displayMonitor.NotifyDisplaySettingsChanged();
                }
            });
        }
        catch (Exception exception)
        {
            _displayMonitor.ReportFault(
                $"Display-settings policy reevaluation failed: {exception.Message}");
        }
    }

    private void StartDisplayPolicyMonitoring()
    {
        try
        {
            _displayMonitor.Start(_windowHandle);
        }
        catch (Exception exception)
        {
            var faultState = _displayMonitor.State with
            {
                IsVisible = true,
                Fault = $"Starting fullscreen policy monitoring failed: {exception.Message}"
            };
            _viewModel.ApplyDisplayPolicyState(faultState);
        }
    }

    private void OnDisplayPolicyRequested(OverlayDisplayPolicy policy)
    {
        try
        {
            _displayMonitor.SetPolicy(policy);
        }
        catch (Exception exception)
        {
            _displayMonitor.ReportFault(
                $"Changing the display policy failed: {exception.Message}");
        }
    }

    private void OnOverlayLocationChanged(object? sender, EventArgs e)
    {
        if (_isClosed || _windowHandle == nint.Zero)
        {
            return;
        }

        try
        {
            _displayMonitor.NotifyOverlayMonitorChanged();
        }
        catch (Exception exception)
        {
            _displayMonitor.ReportFault(
                $"Overlay-monitor reevaluation failed: {exception.Message}");
        }
    }

    private void OnDisplayPolicyStateChanged(OverlayDisplayPolicyState state)
    {
        if (_isClosed)
        {
            return;
        }

        try
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (_isClosed)
                {
                    return;
                }

                ApplyDisplayPolicyState(state);
            });
        }
        catch (Exception exception)
        {
            _displayMonitor.ReportFault(
                $"Dispatching the display policy state failed: {exception.Message}");
        }
    }

    private void ApplyDisplayPolicyState(OverlayDisplayPolicyState state)
    {
        try
        {
            if (_appliedPolicyTopmost != state.IsTopmost)
            {
                Topmost = state.IsTopmost;
                _appliedPolicyTopmost = state.IsTopmost;
            }

            _visibilityCoordinator.SetFullscreenPolicyVisibility(state.IsVisible);
            _viewModel.ApplyDisplayPolicyState(state with
            {
                IsVisible = _visibilityCoordinator.State.IsActuallyVisible
            });
        }
        catch (Exception exception)
        {
            string fault = $"Applying the display policy failed: {exception.Message}";
            _displayMonitor.ReportFault(fault);
            _viewModel.ApplyDisplayPolicyState(state with
            {
                IsVisible = _isActuallyVisible,
                Fault = fault
            });
        }
    }

    private void OnVisibilityStateChanged(WindowVisibilityState state)
    {
        try
        {
            Dispatcher.BeginInvoke(() => ApplyVisibilityState(state));
        }
        catch (Exception exception)
        {
            _viewModel.ReportOverlayError(
                $"Dispatching Dashboard visibility failed: {exception.Message}");
        }
    }

    private void ApplyVisibilityState(WindowVisibilityState state)
    {
        if (_isClosed || state.IsActuallyVisible == _isActuallyVisible)
        {
            return;
        }

        try
        {
            if (state.IsActuallyVisible)
            {
                Show();
                _viewModel.SetAnimationVisibility(true);
            }
            else
            {
                _viewModel.SetAnimationVisibility(false);
                Hide();
            }

            _isActuallyVisible = state.IsActuallyVisible;
            _viewModel.ApplyDisplayPolicyState(_displayMonitor.State with
            {
                IsVisible = _isActuallyVisible
            });
        }
        catch (Exception exception)
        {
            _viewModel.ReportOverlayError(
                $"Applying Dashboard visibility failed: {exception.Message}");
        }
    }

    private void PositionAtInitialWorkAreaLocation()
    {
        try
        {
            Rect primaryWorkArea = SystemParameters.WorkArea;
            var workArea = new WindowWorkArea(
                primaryWorkArea.Left,
                primaryWorkArea.Top,
                primaryWorkArea.Width,
                primaryWorkArea.Height);
            double width = GetWindowWidth();
            double height = GetWindowHeight();
            var desired = new WindowBounds(
                workArea.Right - width - InitialWorkAreaMargin,
                workArea.Top + InitialWorkAreaMargin,
                width,
                height);
            ApplyWindowBounds(WindowPositionClamp.Clamp(desired, workArea));
        }
        catch (Exception exception)
        {
            _viewModel.ReportOverlayError(
                $"Overlay position initialization failed: {exception.Message}");
        }
    }

    private void TryClampToCurrentWorkArea()
    {
        try
        {
            WindowWorkArea workArea = _workAreaProvider.GetForWindow(_windowHandle);
            var current = new WindowBounds(Left, Top, GetWindowWidth(), GetWindowHeight());
            ApplyWindowBounds(WindowPositionClamp.Clamp(current, workArea));
        }
        catch (Exception exception)
        {
            _viewModel.ReportOverlayError(
                $"Overlay position recovery failed: {exception.Message}");
        }
    }

    private double GetWindowWidth()
    {
        return ActualWidth > 0 ? ActualWidth : Width;
    }

    private double GetWindowHeight()
    {
        return ActualHeight > 0 ? ActualHeight : Height;
    }

    private void ApplyWindowBounds(WindowBounds bounds)
    {
        Left = bounds.Left;
        Top = bounds.Top;
    }
}
