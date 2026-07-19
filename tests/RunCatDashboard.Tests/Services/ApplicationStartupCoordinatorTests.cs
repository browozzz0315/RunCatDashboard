using RunCatDashboard.App.Services;

namespace RunCatDashboard.Tests.Services;

public sealed class ApplicationStartupCoordinatorTests
{
    [Fact]
    public void Coordinate_WithOwnership_StartsPrimaryInstanceOnly()
    {
        var guard = new FakeApplicationInstanceGuard(hasOwnership: true);
        var coordinator = new ApplicationStartupCoordinator(guard);
        int primaryStartupCount = 0;
        int secondaryMessageCount = 0;

        ApplicationStartupDecision decision = coordinator.Coordinate(
            () => primaryStartupCount++,
            () => secondaryMessageCount++);

        Assert.Equal(ApplicationStartupDecision.StartPrimaryInstance, decision);
        Assert.Equal(1, primaryStartupCount);
        Assert.Equal(0, secondaryMessageCount);
    }

    [Fact]
    public void Coordinate_WithoutOwnership_DoesNotCreateMainWindowOrStartLifecycle()
    {
        var guard = new FakeApplicationInstanceGuard(hasOwnership: false);
        var coordinator = new ApplicationStartupCoordinator(guard);
        int primaryCompositionAndLifecycleCount = 0;
        int secondaryMessageCount = 0;

        ApplicationStartupDecision decision = coordinator.Coordinate(
            () => primaryCompositionAndLifecycleCount++,
            () => secondaryMessageCount++);

        Assert.Equal(ApplicationStartupDecision.ExitSecondaryInstance, decision);
        Assert.Equal(0, primaryCompositionAndLifecycleCount);
        Assert.Equal(1, secondaryMessageCount);
    }

    [Fact]
    public void Coordinate_WhenRepeated_ShowsSecondaryMessageOnlyOnce()
    {
        var coordinator = new ApplicationStartupCoordinator(
            new FakeApplicationInstanceGuard(hasOwnership: false));
        int primaryStartupCount = 0;
        int secondaryMessageCount = 0;

        coordinator.Coordinate(
            () => primaryStartupCount++,
            () => secondaryMessageCount++);
        coordinator.Coordinate(
            () => primaryStartupCount++,
            () => secondaryMessageCount++);

        Assert.Equal(0, primaryStartupCount);
        Assert.Equal(1, secondaryMessageCount);
    }

    [Fact]
    public void Coordinate_WhenGuardFails_DoesNotSilentlyStartOrShowAlreadyRunning()
    {
        var expected = new ApplicationInstanceException(
            "configured guard failure",
            new InvalidOperationException());
        var coordinator = new ApplicationStartupCoordinator(
            new FakeApplicationInstanceGuard(expected));
        int primaryStartupCount = 0;
        int secondaryMessageCount = 0;

        ApplicationInstanceException exception = Assert.Throws<ApplicationInstanceException>(
            () => coordinator.Coordinate(
                () => primaryStartupCount++,
                () => secondaryMessageCount++));

        Assert.Same(expected, exception);
        Assert.Equal(0, primaryStartupCount);
        Assert.Equal(0, secondaryMessageCount);
    }

    private sealed class FakeApplicationInstanceGuard : IApplicationInstanceGuard
    {
        private readonly bool _hasOwnership;
        private readonly Exception? _exception;

        internal FakeApplicationInstanceGuard(bool hasOwnership)
        {
            _hasOwnership = hasOwnership;
        }

        internal FakeApplicationInstanceGuard(Exception exception)
        {
            _exception = exception;
        }

        public bool HasOwnership => _hasOwnership;

        public bool TryAcquireOwnership()
        {
            return _exception is null ? _hasOwnership : throw _exception;
        }

        public void Dispose()
        {
        }
    }
}
