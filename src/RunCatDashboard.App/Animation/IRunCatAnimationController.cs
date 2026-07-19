namespace RunCatDashboard.App.Animation;

public interface IRunCatAnimationController : IDisposable
{
    int FrameCount { get; }

    int FrameIndex { get; }

    TimeSpan Interval { get; }

    bool IsRunning { get; }

    string? LastFault { get; }

    event Action<int>? FrameChanged;

    event Action<string>? Faulted;

    bool Start();

    void Stop();

    bool UpdateInterval(TimeSpan interval);
}
