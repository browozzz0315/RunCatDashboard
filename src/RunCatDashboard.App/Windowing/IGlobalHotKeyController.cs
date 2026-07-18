namespace RunCatDashboard.App.Windowing;

public interface IGlobalHotKeyController
{
    string GestureText { get; }

    bool IsRegistered { get; }

    string? LastError { get; }

    bool Register(nint windowHandle);

    bool IsTargetMessage(int message, nint parameter);

    bool Unregister();

    void Close();
}
