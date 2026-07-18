using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using RunCatDashboard.App.ViewModels;
using RunCatDashboard.App.Windowing;

namespace RunCatDashboard.App.Views;

public partial class MainWindow : Window
{
    private static readonly TimeSpan ClickThroughSafetyTimeout = TimeSpan.FromSeconds(10);

    private readonly MainWindowViewModel _viewModel;
    private readonly IOverlayWindowController _overlayWindowController;
    private readonly DispatcherTimer _clickThroughSafetyTimer;

    public MainWindow(
        MainWindowViewModel viewModel,
        IOverlayWindowController overlayWindowController)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _overlayWindowController = overlayWindowController;
        DataContext = viewModel;

        _clickThroughSafetyTimer = new DispatcherTimer(
            ClickThroughSafetyTimeout,
            DispatcherPriority.Normal,
            OnClickThroughSafetyTimerTick,
            Dispatcher)
        {
            IsEnabled = false
        };

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Loaded += OnLoaded;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        nint windowHandle = new WindowInteropHelper(this).Handle;
        _overlayWindowController.Initialize(windowHandle);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape &&
            _viewModel.OverlayMode == OverlayInteractionMode.ClickThrough)
        {
            _clickThroughSafetyTimer.Stop();
            _viewModel.TrySetOverlayMode(OverlayInteractionMode.Interactive);
            e.Handled = true;
        }

        base.OnPreviewKeyDown(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        Loaded -= OnLoaded;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _clickThroughSafetyTimer.Stop();
        _clickThroughSafetyTimer.Tick -= OnClickThroughSafetyTimerTick;
        _overlayWindowController.Close();
        _viewModel.DisposeAsync().AsTask().GetAwaiter().GetResult();
        base.OnClosed(e);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        _viewModel.Start();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainWindowViewModel.OverlayMode))
        {
            return;
        }

        if (_viewModel.OverlayMode == OverlayInteractionMode.ClickThrough)
        {
            _clickThroughSafetyTimer.Stop();
            _clickThroughSafetyTimer.Start();
        }
        else
        {
            _clickThroughSafetyTimer.Stop();
        }
    }

    private void OnClickThroughSafetyTimerTick(object? sender, EventArgs e)
    {
        _clickThroughSafetyTimer.Stop();
        _viewModel.TrySetOverlayMode(OverlayInteractionMode.Interactive);
    }

    private void OnDragSurfaceMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel.IsInteractive && e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void OnCloseButtonClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
