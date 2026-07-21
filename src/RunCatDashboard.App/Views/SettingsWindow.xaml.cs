using System.Windows;
using RunCatDashboard.App.ViewModels;
using RunCatDashboard.App.Windowing;

namespace RunCatDashboard.App.Views;

public partial class SettingsWindow : Window, ISettingsWindowHost
{
    private readonly SettingsWindowViewModel _viewModel;

    public SettingsWindow(SettingsWindowViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        _viewModel.CloseRequested += OnCloseRequested;
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.CloseRequested -= OnCloseRequested;
        base.OnClosed(e);
    }

    private void OnCloseRequested() => Close();

    bool ISettingsWindowHost.IsMinimized => WindowState == WindowState.Minimized;
    void ISettingsWindowHost.Activate() => Activate();
    void ISettingsWindowHost.Restore() => WindowState = WindowState.Normal;
}
