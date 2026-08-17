using System.Diagnostics;
using System.IO;

namespace RunCatDashboard.App.Services;

public interface IApplicationPaths
{
    string DataDirectory { get; }

    string LogsDirectory { get; }

    string AnimationsDirectory { get; }

    int WindowsSessionId { get; }
}

internal sealed class ApplicationPaths : IApplicationPaths
{
    internal const string ApplicationDirectoryName = "RunCatDashboard";
    internal const string LogsDirectoryName = "Logs";
    internal const string AnimationsDirectoryName = "Animations";

    internal ApplicationPaths(string localApplicationDataDirectory, int windowsSessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localApplicationDataDirectory);
        ArgumentOutOfRangeException.ThrowIfNegative(windowsSessionId);

        DataDirectory = Path.Combine(
            Path.GetFullPath(localApplicationDataDirectory),
            ApplicationDirectoryName);
        LogsDirectory = Path.Combine(DataDirectory, LogsDirectoryName);
        AnimationsDirectory = Path.Combine(DataDirectory, AnimationsDirectoryName);
        WindowsSessionId = windowsSessionId;
    }

    public string DataDirectory { get; }

    public string LogsDirectory { get; }

    public string AnimationsDirectory { get; }

    public int WindowsSessionId { get; }

    internal static ApplicationPaths CreateDefault()
    {
        string localApplicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.Create);
        return new ApplicationPaths(
            localApplicationData,
            Process.GetCurrentProcess().SessionId);
    }
}
