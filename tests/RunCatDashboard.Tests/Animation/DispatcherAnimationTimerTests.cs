using System.Windows.Threading;
using RunCatDashboard.App.Animation;

namespace RunCatDashboard.Tests.Animation;

public sealed class DispatcherAnimationTimerTests
{
    [Fact]
    public void LifecycleOperations_OnOwningDispatcherThread_AreAccepted()
    {
        Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
        var timer = new DispatcherAnimationTimer(dispatcher);

        Assert.True(timer.Start(
            TimeSpan.FromMilliseconds(250),
            () => { },
            _ => { }));
        Assert.True(timer.UpdateInterval(TimeSpan.FromMilliseconds(100)));
        timer.Stop();
        timer.Dispose();
    }

    [Fact]
    public void LifecycleOperation_OutsideOwningDispatcherThread_IsRejected()
    {
        Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
        var timer = new DispatcherAnimationTimer(dispatcher);

        Exception? exception = null;
        var foreignThread = new Thread(
            () => exception = Record.Exception(
                () => timer.Start(
                    TimeSpan.FromMilliseconds(250),
                    () => { },
                    _ => { })));
        foreignThread.Start();
        Assert.True(foreignThread.Join(TimeSpan.FromSeconds(5)));

        Assert.IsType<InvalidOperationException>(exception);
        timer.Dispose();
    }
}
