namespace Neomotive.ModuleSimulator;

public record CanPacketEntry(DateTime Timestamp, int Id, byte[] Data, bool IsOutgoing);

public class CanPacketLog
{
    private int _maxDepth;
    private readonly Queue<CanPacketEntry> _entries = new();
    private readonly object _lock = new();

    public CanPacketLog(int maxDepth = 100)
    {
        _maxDepth = maxDepth;
    }

    public int MaxDepth
    {
        get => _maxDepth;
        set
        {
            lock (_lock)
            {
                _maxDepth = value < 1 ? 1 : value;
                while (_entries.Count > _maxDepth)
                    _entries.Dequeue();
            }
        }
    }

    public void Add(CanPacketEntry entry)
    {
        lock (_lock)
        {
            _entries.Enqueue(entry);
            while (_entries.Count > _maxDepth)
                _entries.Dequeue();
        }
    }

    public void Clear()
    {
        lock (_lock)
            _entries.Clear();
    }

    public IReadOnlyList<CanPacketEntry> GetAll()
    {
        lock (_lock)
            return _entries.ToList();
    }

    public IReadOnlyList<CanPacketEntry> GetRecent(int count)
    {
        lock (_lock)
            return _entries.TakeLast(count).ToList();
    }
}
