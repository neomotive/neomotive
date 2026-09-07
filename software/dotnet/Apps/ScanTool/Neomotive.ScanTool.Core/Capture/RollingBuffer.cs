namespace Neomotive.ScanTool.Core.Capture;

/// <summary>
/// Fixed-capacity ring that overwrites the oldest entry once full. Holds the pre-trigger history
/// so that arming before a start attempt still captures the key-on prime phase that precedes it.
/// </summary>
/// <remarks>
/// Guarded by a simple lock. Sample rates here are tens per second, so contention is immaterial
/// and correctness is worth more than a lock-free design.
/// </remarks>
public sealed class RollingBuffer<T>
{
    private readonly T[] _items;
    private readonly object _sync = new();
    private int _next;
    private int _count;

    public RollingBuffer(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be positive.");
        }

        _items = new T[capacity];
    }

    public int Capacity => _items.Length;

    public int Count
    {
        get { lock (_sync) { return _count; } }
    }

    public bool IsFull
    {
        get { lock (_sync) { return _count == _items.Length; } }
    }

    public void Add(T item)
    {
        lock (_sync)
        {
            _items[_next] = item;
            _next = (_next + 1) % _items.Length;

            if (_count < _items.Length)
            {
                _count++;
            }
        }
    }

    /// <summary>Returns the buffered items in chronological order, oldest first.</summary>
    public T[] ToArray()
    {
        lock (_sync)
        {
            var result = new T[_count];

            // When full, the oldest entry sits at the write cursor; otherwise writing started at 0.
            var start = _count == _items.Length ? _next : 0;

            for (var i = 0; i < _count; i++)
            {
                result[i] = _items[(start + i) % _items.Length];
            }

            return result;
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            Array.Clear(_items);
            _next = 0;
            _count = 0;
        }
    }
}
