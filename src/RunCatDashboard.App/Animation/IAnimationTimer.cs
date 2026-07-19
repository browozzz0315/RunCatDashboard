namespace RunCatDashboard.App.Animation;

internal interface IAnimationTimer : IDisposable
{
    bool Start(
        TimeSpan interval,
        Action callback,
        Action<string> faultCallback);

    bool UpdateInterval(TimeSpan interval);

    void Stop();
}
