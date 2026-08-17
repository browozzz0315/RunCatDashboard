using System.Threading;
using System.Windows.Threading;

namespace RunCatDashboard.Tests.Support;

internal sealed class DispatcherTestThread : IDisposable
{
    private readonly Thread _thread;
    private readonly TaskCompletionSource<Dispatcher> _dispatcherReady =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _dispatcherStopped =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _isDisposed;

    internal DispatcherTestThread()
    {
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "RunCatDashboard.Tests.Dispatcher"
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    internal Dispatcher Dispatcher =>
        _dispatcherReady.Task.GetAwaiter().GetResult();

    internal T Invoke<T>(Func<T> action) => Dispatcher.Invoke(action);

    internal void Invoke(Action action) => Dispatcher.Invoke(action);

    internal Task<T> InvokeAsync<T>(Func<T> action) =>
        Dispatcher.InvokeAsync(action).Task;

    internal void BeginInvoke(Action action) => Dispatcher.BeginInvoke(action);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
        {
            return;
        }

        Dispatcher.BeginInvokeShutdown(DispatcherPriority.Normal);
        if (!_dispatcherStopped.Task.Wait(TimeSpan.FromSeconds(5)))
        {
            throw new TimeoutException("The test dispatcher did not stop.");
        }

        if (!_thread.Join(TimeSpan.FromSeconds(5)))
        {
            throw new TimeoutException("The test dispatcher thread did not stop.");
        }
    }

    private void Run()
    {
        try
        {
            Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
            _dispatcherReady.TrySetResult(dispatcher);
            Dispatcher.Run();
        }
        catch (Exception exception)
        {
            _dispatcherReady.TrySetException(exception);
        }
        finally
        {
            _dispatcherStopped.TrySetResult();
        }
    }
}
