using System.Windows;
using RunCatDashboard.App.ViewModels;

namespace RunCatDashboard.App.Views;

public partial class MainWindow : Window
{
    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
