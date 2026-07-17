using RunCatDashboard.App.Collections;
using RunCatDashboard.App.Models;

namespace RunCatDashboard.Tests.Collections;

public sealed class BoundedHistoryTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_WithInvalidCapacity_ThrowsArgumentOutOfRangeException(int capacity)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BoundedHistory<int>(capacity));
    }

    [Fact]
    public void Constructor_WithValidCapacity_StartsEmptyAndExposesCapacity()
    {
        var history = new BoundedHistory<int>(3);

        Assert.Equal(0, history.Count);
        Assert.Equal(3, history.Capacity);
        Assert.Empty(history.GetSnapshot());
    }

    [Fact]
    public void Add_BelowCapacity_PreservesInsertionOrder()
    {
        var history = new BoundedHistory<int>(3);

        history.Add(10);
        history.Add(20);

        Assert.Equal(2, history.Count);
        Assert.Equal([10, 20], history.GetSnapshot());
    }

    [Fact]
    public void Add_AtCapacity_RetainsAllItems()
    {
        var history = new BoundedHistory<int>(3);

        history.Add(10);
        history.Add(20);
        history.Add(30);

        Assert.Equal(3, history.Count);
        Assert.Equal([10, 20, 30], history.GetSnapshot());
    }

    [Fact]
    public void Add_AboveCapacity_RemovesOldestItem()
    {
        var history = new BoundedHistory<int>(3);
        history.Add(10);
        history.Add(20);
        history.Add(30);

        history.Add(40);

        Assert.Equal(3, history.Count);
        Assert.Equal([20, 30, 40], history.GetSnapshot());
    }

    [Fact]
    public void Add_RepeatedlyAboveCapacity_RetainsOnlyNewestItems()
    {
        var history = new BoundedHistory<int>(3);

        for (int value = 1; value <= 100; value++)
        {
            history.Add(value);
        }

        Assert.Equal(3, history.Count);
        Assert.Equal([98, 99, 100], history.GetSnapshot());
    }

    [Fact]
    public void Add_WithCapacityOne_RetainsOnlyNewestItem()
    {
        var history = new BoundedHistory<int>(1);

        history.Add(10);
        history.Add(20);

        Assert.Equal(1, history.Count);
        Assert.Equal([20], history.GetSnapshot());
    }

    [Fact]
    public void Clear_RemovesItemsAndPreservesCapacity()
    {
        var history = new BoundedHistory<int>(2);
        history.Add(10);
        history.Add(20);

        history.Clear();

        Assert.Equal(0, history.Count);
        Assert.Equal(2, history.Capacity);
        Assert.Empty(history.GetSnapshot());
    }

    [Fact]
    public void Clear_AllowsItemsToBeAddedAgain()
    {
        var history = new BoundedHistory<int>(2);
        history.Add(10);
        history.Clear();

        history.Add(20);

        Assert.Equal(1, history.Count);
        Assert.Equal([20], history.GetSnapshot());
    }

    [Fact]
    public void GetSnapshot_ReturnsReadOnlyCollectionWithoutExposingInternalState()
    {
        var history = new BoundedHistory<int>(2);
        history.Add(10);
        history.Add(20);
        IReadOnlyList<int> snapshot = history.GetSnapshot();
        var collection = Assert.IsAssignableFrom<IList<int>>(snapshot);

        Assert.True(collection.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => collection[0] = 99);
        Assert.Equal([10, 20], history.GetSnapshot());
    }

    [Fact]
    public void GetSnapshot_PreviousResultIsUnaffectedBySubsequentAddOrClear()
    {
        var history = new BoundedHistory<int>(2);
        history.Add(10);
        history.Add(20);
        IReadOnlyList<int> snapshot = history.GetSnapshot();

        history.Add(30);
        history.Clear();

        Assert.Equal([10, 20], snapshot);
        Assert.Empty(history.GetSnapshot());
    }

    [Fact]
    public void Add_WithSystemMetricsSnapshot_PreservesSnapshot()
    {
        var history = new BoundedHistory<SystemMetricsSnapshot>(2);
        var metrics = new SystemMetricsSnapshot(
            new DateTimeOffset(2026, 7, 18, 12, 0, 0, TimeSpan.Zero),
            42.5d,
            75d,
            6_000,
            8_000);

        history.Add(metrics);

        SystemMetricsSnapshot stored = Assert.Single(history.GetSnapshot());
        Assert.Equal(metrics, stored);
    }
}
