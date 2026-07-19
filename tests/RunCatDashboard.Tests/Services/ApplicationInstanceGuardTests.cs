using RunCatDashboard.App.Services;

namespace RunCatDashboard.Tests.Services;

public sealed class ApplicationInstanceGuardTests
{
    [Fact]
    public void FirstGuard_WithUniqueName_AcquiresOwnership()
    {
        using WindowsApplicationInstanceGuard guard = CreateGuard(UniqueMutexName());

        Assert.True(guard.TryAcquireOwnership());
        Assert.True(guard.HasOwnership);
    }

    [Fact]
    public void SecondGuard_WithSameName_CannotAcquireOwnershipImmediately()
    {
        string mutexName = UniqueMutexName();
        using var owner = new OwnedGuardThread(mutexName);
        using WindowsApplicationInstanceGuard second = CreateGuard(mutexName);

        bool acquired = second.TryAcquireOwnership();

        Assert.False(acquired);
        Assert.False(second.HasOwnership);
    }

    [Fact]
    public void NewGuard_AfterOwnerDisposes_CanAcquireOwnership()
    {
        string mutexName = UniqueMutexName();
        using (var owner = new OwnedGuardThread(mutexName))
        {
            Assert.False(TryAcquireOnCurrentThread(mutexName));
        }

        Assert.True(TryAcquireOnCurrentThread(mutexName));
    }

    [Fact]
    public void Dispose_WhenRepeated_IsSafe()
    {
        WindowsApplicationInstanceGuard guard = CreateGuard(UniqueMutexName());
        Assert.True(guard.TryAcquireOwnership());

        guard.Dispose();
        guard.Dispose();

        Assert.False(guard.HasOwnership);
    }

    [Fact]
    public void NonOwnerDispose_DoesNotReleaseOwnerOwnership()
    {
        string mutexName = UniqueMutexName();
        using var owner = new OwnedGuardThread(mutexName);
        WindowsApplicationInstanceGuard nonOwner = CreateGuard(mutexName);
        Assert.False(nonOwner.TryAcquireOwnership());

        nonOwner.Dispose();

        Assert.False(TryAcquireOnCurrentThread(mutexName));
    }

    [Fact]
    public void AbandonedMutex_IsTreatedAsAcquiredOwnership()
    {
        var mutex = new FakeApplicationInstanceMutex
        {
            WaitException = new AbandonedMutexException()
        };
        var guard = new WindowsApplicationInstanceGuard(
            new FakeApplicationInstanceMutexFactory(mutex),
            UniqueMutexName());

        bool acquired = guard.TryAcquireOwnership();
        guard.Dispose();

        Assert.True(acquired);
        Assert.Equal(1, mutex.ReleaseCount);
        Assert.Equal(1, mutex.DisposeCount);
    }

    [Fact]
    public void MutexCreationFailure_IsDiagnosticAndDoesNotClaimOwnership()
    {
        var guard = new WindowsApplicationInstanceGuard(
            new ThrowingApplicationInstanceMutexFactory(
                new UnauthorizedAccessException("configured access failure")),
            UniqueMutexName());

        ApplicationInstanceException exception =
            Assert.Throws<ApplicationInstanceException>(
                () => guard.TryAcquireOwnership());

        Assert.Contains("single-instance guard", exception.Message);
        Assert.Contains("configured access failure", exception.Message);
        Assert.False(guard.HasOwnership);
    }

    [Fact]
    public void OwnershipCheckFailure_IsDiagnosticAndDisposesMutex()
    {
        var mutex = new FakeApplicationInstanceMutex
        {
            WaitException = new InvalidOperationException("configured wait failure")
        };
        var guard = new WindowsApplicationInstanceGuard(
            new FakeApplicationInstanceMutexFactory(mutex),
            UniqueMutexName());

        ApplicationInstanceException exception =
            Assert.Throws<ApplicationInstanceException>(
                () => guard.TryAcquireOwnership());

        Assert.Contains("configured wait failure", exception.Message);
        Assert.False(guard.HasOwnership);
        Assert.Equal(0, mutex.ReleaseCount);
        Assert.Equal(1, mutex.DisposeCount);
    }

    [Fact]
    public void InitializationFailure_WhenRetried_RemainsDiagnostic()
    {
        var guard = new WindowsApplicationInstanceGuard(
            new ThrowingApplicationInstanceMutexFactory(
                new InvalidOperationException("configured initialization failure")),
            UniqueMutexName());
        ApplicationInstanceException first = Assert.Throws<ApplicationInstanceException>(
            () => guard.TryAcquireOwnership());

        ApplicationInstanceException second = Assert.Throws<ApplicationInstanceException>(
            () => guard.TryAcquireOwnership());

        Assert.Same(first, second);
        Assert.False(guard.HasOwnership);
    }

    [Fact]
    public void ProductionMutexName_IsFixedProjectSpecificAndSessionLocal()
    {
        Assert.Equal(
            @"Local\RunCatDashboard.SingleInstance",
            WindowsApplicationInstanceGuard.MutexName);
    }

    private static WindowsApplicationInstanceGuard CreateGuard(string mutexName)
    {
        return new WindowsApplicationInstanceGuard(
            new WindowsApplicationInstanceMutexFactory(),
            mutexName);
    }

    private static bool TryAcquireOnCurrentThread(string mutexName)
    {
        using WindowsApplicationInstanceGuard guard = CreateGuard(mutexName);
        return guard.TryAcquireOwnership();
    }

    private static string UniqueMutexName() =>
        $@"Local\RunCatDashboard.Tests.{Guid.NewGuid():N}";

    private sealed class OwnedGuardThread : IDisposable
    {
        private readonly ManualResetEventSlim _release = new(false);
        private readonly Thread _thread;
        private Exception? _failure;

        internal OwnedGuardThread(string mutexName)
        {
            var acquired = new ManualResetEventSlim(false);
            _thread = new Thread(() =>
            {
                try
                {
                    using WindowsApplicationInstanceGuard guard = CreateGuard(mutexName);
                    if (!guard.TryAcquireOwnership())
                    {
                        throw new InvalidOperationException("The owner thread did not acquire the test mutex.");
                    }

                    acquired.Set();
                    _release.Wait();
                }
                catch (Exception exception)
                {
                    _failure = exception;
                    acquired.Set();
                }
            })
            {
                IsBackground = true
            };
            _thread.Start();
            Assert.True(acquired.Wait(TimeSpan.FromSeconds(5)));
            if (_failure is not null)
            {
                throw new InvalidOperationException("Starting the mutex owner thread failed.", _failure);
            }
        }

        public void Dispose()
        {
            _release.Set();
            Assert.True(_thread.Join(TimeSpan.FromSeconds(5)));
            if (_failure is not null)
            {
                throw new InvalidOperationException("The mutex owner thread failed.", _failure);
            }

            _release.Dispose();
        }
    }

    private sealed class FakeApplicationInstanceMutexFactory(
        IApplicationInstanceMutex mutex) : IApplicationInstanceMutexFactory
    {
        public IApplicationInstanceMutex Create(string name) => mutex;
    }

    private sealed class ThrowingApplicationInstanceMutexFactory(
        Exception exception) : IApplicationInstanceMutexFactory
    {
        public IApplicationInstanceMutex Create(string name) => throw exception;
    }

    private sealed class FakeApplicationInstanceMutex : IApplicationInstanceMutex
    {
        internal Exception? WaitException { get; init; }
        internal int ReleaseCount { get; private set; }
        internal int DisposeCount { get; private set; }

        public bool WaitOne(int millisecondsTimeout)
        {
            Assert.Equal(0, millisecondsTimeout);
            if (WaitException is not null)
            {
                throw WaitException;
            }

            return true;
        }

        public void ReleaseMutex() => ReleaseCount++;

        public void Dispose() => DisposeCount++;
    }
}
