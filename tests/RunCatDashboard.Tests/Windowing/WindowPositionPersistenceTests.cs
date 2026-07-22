using RunCatDashboard.App.Windowing;

namespace RunCatDashboard.Tests.Windowing;

public sealed class WindowPositionPersistenceTests
{
    [Fact]
    public void RestoreInProgress_DoesNotSavePosition() =>
        Assert.False(WindowPositionPersistence.ShouldSave(false, 100, 200));

    [Fact]
    public void NegativeFiniteCoordinates_AreAccepted() =>
        Assert.True(WindowPositionPersistence.ShouldSave(true, -1920, -240));

    [Theory]
    [InlineData(double.NaN, 0)]
    [InlineData(0, double.PositiveInfinity)]
    public void NonFiniteCoordinate_IsRejected(double left, double top) =>
        Assert.False(WindowPositionPersistence.ShouldSave(true, left, top));
}
