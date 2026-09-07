using Neomotive.ScanTool.Core.Capture;
using Xunit;

namespace Neomotive.ScanTool.Core.Tests;

public class RollingBufferTests
{
    [Fact]
    public void Zero_capacity_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RollingBuffer<int>(0));
    }

    [Fact]
    public void Returns_items_in_chronological_order_when_partially_filled()
    {
        var buffer = new RollingBuffer<int>(5);
        buffer.Add(1);
        buffer.Add(2);
        buffer.Add(3);

        Assert.Equal(new[] { 1, 2, 3 }, buffer.ToArray());
        Assert.Equal(3, buffer.Count);
        Assert.False(buffer.IsFull);
    }

    [Fact]
    public void Oldest_entries_are_overwritten_once_full()
    {
        var buffer = new RollingBuffer<int>(3);

        for (var i = 1; i <= 5; i++)
        {
            buffer.Add(i);
        }

        // 1 and 2 have aged out; order must still read oldest-first.
        Assert.Equal(new[] { 3, 4, 5 }, buffer.ToArray());
        Assert.Equal(3, buffer.Count);
        Assert.True(buffer.IsFull);
    }

    [Fact]
    public void Wrapping_many_times_preserves_order()
    {
        var buffer = new RollingBuffer<int>(4);

        for (var i = 0; i < 103; i++)
        {
            buffer.Add(i);
        }

        Assert.Equal(new[] { 99, 100, 101, 102 }, buffer.ToArray());
    }

    [Fact]
    public void Clear_empties_the_buffer_and_resets_ordering()
    {
        var buffer = new RollingBuffer<int>(3);
        buffer.Add(1);
        buffer.Add(2);
        buffer.Add(3);
        buffer.Add(4);

        buffer.Clear();
        Assert.Empty(buffer.ToArray());

        buffer.Add(9);
        Assert.Equal(new[] { 9 }, buffer.ToArray());
    }
}
