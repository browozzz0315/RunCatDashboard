namespace RunCatDashboard.App.Windowing;

public interface IOverlayHotKeyMessageHandler
{
    bool TryHandleMessage(int message, nint parameter);
}
