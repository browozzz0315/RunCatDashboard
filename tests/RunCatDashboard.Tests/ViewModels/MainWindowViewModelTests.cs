using RunCatDashboard.App.ViewModels;

namespace RunCatDashboard.Tests.ViewModels;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public void StatusMessage_WhenCreated_DescribesReadyFoundation()
    {
        var viewModel = new MainWindowViewModel();

        Assert.Equal("RunCatDashboard MVVM foundation is ready.", viewModel.StatusMessage);
    }
}
