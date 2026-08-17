using System.IO;
using RunCatDashboard.App.Services;

namespace RunCatDashboard.Tests.Services;

public sealed class ApplicationPathsTests
{
    [Fact]
    public void Constructor_CentralizesDataAndLogsUnderLocalApplicationDataRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        var paths = new ApplicationPaths(root, windowsSessionId: 42);

        Assert.Equal(Path.Combine(root, "RunCatDashboard"), paths.DataDirectory);
        Assert.Equal(Path.Combine(root, "RunCatDashboard", "Logs"), paths.LogsDirectory);
        Assert.Equal(
            Path.Combine(root, "RunCatDashboard", "Animations"),
            paths.AnimationsDirectory);
        Assert.Equal(42, paths.WindowsSessionId);
        Assert.False(paths.LogsDirectory.StartsWith(AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DefaultPaths_UseWindowsLocalApplicationData()
    {
        ApplicationPaths paths = ApplicationPaths.CreateDefault();
        string localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.Create);

        Assert.StartsWith(Path.GetFullPath(localAppData), paths.DataDirectory, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(Path.Combine("RunCatDashboard", "Logs"), paths.LogsDirectory);
    }
}
