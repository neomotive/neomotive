namespace Neomotive.ModuleSimulator.Desktop;

public record CanPacketEntry(DateTime Timestamp, int Id, byte[] Data, bool IsOutgoing);

public class CanPacketLog
{
    private const int MaxEntries = 50;
    private readonly Queue<CanPacketEntry> _entries = new();
    private readonly object _lock = new();

    public void Add(CanPacketEntry entry)
    {
        lock (_lock)
        {
            _entries.Enqueue(entry);
            while (_entries.Count > MaxEntries)
                _entries.Dequeue();
        }
    }

    public IReadOnlyList<CanPacketEntry> GetRecent(int count)
    {
        lock (_lock)
        {
            return _entries.TakeLast(count).ToList();
        }
    }
}
