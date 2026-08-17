using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using RunCatDashboard.App.ViewModels;

namespace RunCatDashboard.App.Views;

public partial class AnimationImportWindow : Window
{
    private readonly AnimationImportWindowViewModel _viewModel;
    private readonly DispatcherTimer _previewTimer;

    public AnimationImportWindow(AnimationImportWindowViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        _previewTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(250),
            DispatcherPriority.Background,
            OnPreviewTimerTick,
            Dispatcher);
        _viewModel.CloseRequested += OnCloseRequested;
        Closed += OnClosed;
        Closing += OnClosing;
        _previewTimer.Start();
    }

    private void OnPreviewTimerTick(object? sender, EventArgs e) =>
        _viewModel.AdvancePreviewFrame();

    private void OnCloseRequested() => Close();

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        _previewTimer.Stop();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _previewTimer.Stop();
        _previewTimer.Tick -= OnPreviewTimerTick;
        _viewModel.CloseRequested -= OnCloseRequested;
        Closed -= OnClosed;
        Closing -= OnClosing;
    }
}
