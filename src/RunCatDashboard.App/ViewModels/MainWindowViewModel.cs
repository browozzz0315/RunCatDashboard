using CommunityToolkit.Mvvm.ComponentModel;

namespace RunCatDashboard.App.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    public string StatusMessage { get; } = "RunCatDashboard MVVM foundation is ready.";
}
