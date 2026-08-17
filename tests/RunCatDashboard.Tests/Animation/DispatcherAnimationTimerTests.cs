using System.Windows.Threading;
using RunCatDashboard.App.Animation;
using RunCatDashboard.Tests.Support;

namespace RunCatDashboard.Tests.Animation;

public sealed class DispatcherAnimationTimerTests
{
    [Fact]
    public void LifecycleOperations_OnOwningDispatcherThread_AreAccepted()
    {
        using var dispatcherThread = new DispatcherTestThread();
        DispatcherAnimationTimer timer = dispatcherThread.Invoke(
            () => new DispatcherAnimationTimer(dispatcherThread.Dispatcher));

        try
        {
            Assert.True(dispatcherThread.Invoke(() => timer.Start(
                TimeSpan.FromMilliseconds(250),
                () => { },
                _ => { })));
            Assert.True(dispatcherThread.Invoke(
                () => timer.UpdateInterval(TimeSpan.FromMilliseconds(100))));
            dispatcherThread.Invoke(timer.Stop);
        }
        finally
        {
            dispatcherThread.Invoke(timer.Dispose);
        }
    }

    [Fact]
    public async Task UpdateInterval_FromWorkerThread_IsAppliedOnOwningDispatcher()
    {
        using var dispatcherThread = new DispatcherTestThread();
        DispatcherAnimationTimer timer = dispatcherThread.Invoke(
            () => new DispatcherAnimationTimer(dispatcherThread.Dispatcher));

        try
        {
            dispatcherThread.Invoke(() => timer.Start(
                TimeSpan.FromSeconds(10),
                () => { },
                _ => { }));

            TimeSpan requested = TimeSpan.FromMilliseconds(75);
            Exception? exception = await Task.Run(
                () => Record.Exception(() => timer.UpdateInterval(requested)));

            Assert.Null(exception);
            Assert.Equal(requested, dispatcherThread.Invoke(() => timer.CurrentInterval));
        }
        finally
        {
            await Task.Run(timer.Dispose);
        }
    }

    [Fact]
    public async Task RepeatedWorkerUpdates_KeepLatestRequestedInterval()
    {
        using var dispatcherThread = new DispatcherTestThread();
        DispatcherAnimationTimer timer = dispatcherThread.Invoke(
            () => new DispatcherAnimationTimer(dispatcherThread.Dispatcher));

        try
        {
            dispatcherThread.Invoke(() => timer.Start(
                TimeSpan.FromSeconds(10),
                () => { },
                _ => { }));

            foreach (TimeSpan interval in new[]
            {
                TimeSpan.FromMilliseconds(250),
                TimeSpan.FromMilliseconds(125),
                TimeSpan.FromMilliseconds(50)
            })
            {
                Exception? exception = await Task.Run(
                    () => Record.Exception(() => timer.UpdateInterval(interval)));
                Assert.Null(exception);
            }

            Assert.Equal(
                TimeSpan.FromMilliseconds(50),
                dispatcherThread.Invoke(() => timer.CurrentInterval));
        }
        finally
        {
            await Task.Run(timer.Dispose);
        }
    }

    [Fact]
    public async Task UpdateInterval_OnOwningDispatcher_CompletesWithoutDeadlock()
    {
        using var dispatcherThread = new DispatcherTestThread();
        DispatcherAnimationTimer timer = dispatcherThread.Invoke(
            () => new DispatcherAnimationTimer(dispatcherThread.Dispatcher));

        try
        {
            Task<bool> update = dispatcherThread.InvokeAsync(
                () => timer.UpdateInterval(TimeSpan.FromMilliseconds(125)));

            Assert.True(await update.WaitAsync(TimeSpan.FromSeconds(5)));
        }
        finally
        {
            dispatcherThread.Invoke(timer.Dispose);
        }
    }

    [Fact]
    public async Task QueuedWorkerUpdate_AfterDispatcherDisposal_DoesNotMutateDisposedTimer()
    {
        using var dispatcherThread = new DispatcherTestThread();
        DispatcherAnimationTimer timer = dispatcherThread.Invoke(
            () => new DispatcherAnimationTimer(dispatcherThread.Dispatcher));
        var dispatcherBlocked = new ManualResetEventSlim();
        var releaseDispatcher = new ManualResetEventSlim();
        var disposalCompleted = new ManualResetEventSlim();
        TimeSpan intervalAtDisposal = default;
        Exception? updateException = null;
        Task? update = null;

        try
        {
            dispatcherThread.Invoke(() => timer.Start(
                TimeSpan.FromMilliseconds(100),
                () => { },
                _ => { }));

            dispatcherThread.BeginInvoke(() =>
            {
                dispatcherBlocked.Set();
                releaseDispatcher.Wait();
            });
            Assert.True(dispatcherBlocked.Wait(TimeSpan.FromSeconds(5)));

            dispatcherThread.BeginInvoke(() =>
            {
                timer.Dispose();
                intervalAtDisposal = timer.CurrentInterval;
                disposalCompleted.Set();
            });
            var updateStarted = new ManualResetEventSlim();
            update = Task.Run(() =>
            {
                updateStarted.Set();
                updateException = Record.Exception(
                    () => timer.UpdateInterval(TimeSpan.FromMilliseconds(50)));
            });

            Assert.True(updateStarted.Wait(TimeSpan.FromSeconds(5)));
            releaseDispatcher.Set();
            await update.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(disposalCompleted.Wait(TimeSpan.FromSeconds(5)));

            if (updateException is not null)
            {
                Assert.IsType<ObjectDisposedException>(updateException);
            }
            Assert.Equal(
                intervalAtDisposal,
                dispatcherThread.Invoke(() => timer.CurrentInterval));
        }
        finally
        {
            releaseDispatcher.Set();
            if (update is not null)
            {
                await Task.Run(() =>
                {
                    if (updateException is null)
                    {
                        Record.Exception(timer.Dispose);
                    }
                });
            }

            dispatcherBlocked.Dispose();
            releaseDispatcher.Dispose();
            disposalCompleted.Dispose();
        }
    }
}
