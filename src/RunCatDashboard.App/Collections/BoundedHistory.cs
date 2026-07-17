using System.Collections.ObjectModel;

namespace RunCatDashboard.App.Collections;

/// <summary>
/// Stores up to a fixed number of items in insertion order.
/// </summary>
/// <remarks>
/// This type is not thread-safe. Callers must synchronize access when an instance is shared
/// across threads.
/// </remarks>
public sealed class BoundedHistory<T>
{
    private readonly Queue<T> _items;

    public BoundedHistory(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);

        Capacity = capacity;
        _items = new Queue<T>(capacity);
    }

    public int Count => _items.Count;

    public int Capacity { get; }

    public void Add(T item)
    {
        if (_items.Count == Capacity)
        {
            _items.Dequeue();
        }

        _items.Enqueue(item);
    }

    public IReadOnlyList<T> GetSnapshot()
    {
        return new ReadOnlyCollection<T>(_items.ToArray());
    }

    public void Clear()
    {
        _items.Clear();
    }
}
