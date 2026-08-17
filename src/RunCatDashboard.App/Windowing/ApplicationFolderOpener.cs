using System.Diagnostics;

namespace RunCatDashboard.App.Windowing;

internal interface IApplicationFolderOpener
{
    void Open(string directoryPath);
}

internal sealed class WindowsApplicationFolderOpener : IApplicationFolderOpener
{
    public void Open(string directoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        using Process? process = Process.Start(new ProcessStartInfo
        {
            FileName = directoryPath,
            UseShellExecute = true
        });
    }
}
