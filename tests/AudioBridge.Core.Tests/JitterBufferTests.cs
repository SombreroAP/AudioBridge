using AudioBridge.Core;

namespace AudioBridge.Core.Tests;

public class JitterBufferTests
{
    private static AudioPacket Packet(uint sequence) =>
        new(StreamId.GameAudio, sequence, sequence * 480, PacketFlags.None, new byte[] { (byte)sequence });

    [Fact]
    public void HoldsBackUntilPrimed()
    {
        var buffer = new JitterBuffer(targetDepth: 3);
        buffer.Push(Packet(0));
        buffer.Push(Packet(1));
        Assert.False(buffer.IsPrimed);
        Assert.False(buffer.TryDequeue(out _));

        buffer.Push(Packet(2));
        Assert.True(buffer.IsPrimed);
        Assert.True(buffer.TryDequeue(out var first));
        Assert.Equal(0u, first.Sequence);
    }

    [Fact]
    public void ReordersPacketsThatArriveOutOfOrder()
    {
        var buffer = new JitterBuffer(targetDepth: 3);
        foreach (var sequence in new uint[] { 2, 0, 1 })
            buffer.Push(Packet(sequence));

        for (uint expected = 0; expected < 3; expected++)
        {
            Assert.True(buffer.TryDequeue(out var packet));
            Assert.Equal(expected, packet.Sequence);
        }
    }

    [Fact]
    public void DropsPacketsThatArriveAfterTheirSlotPlayed()
    {
        var buffer = new JitterBuffer(targetDepth: 2);
        buffer.Push(Packet(0));
        buffer.Push(Packet(1));
        buffer.TryDequeue(out _); // plays 0

        Assert.False(buffer.Push(Packet(0)));
        Assert.Equal(1, buffer.LatePacketCount);
    }

    [Fact]
    public void IgnoresDuplicates()
    {
        var buffer = new JitterBuffer(targetDepth: 2);
        Assert.True(buffer.Push(Packet(5)));
        Assert.False(buffer.Push(Packet(5)));
        Assert.Equal(1, buffer.Count);
    }

    [Fact]
    public void ConcealsALostPacketOnceLaterOnesPileUp()
    {
        var buffer = new JitterBuffer(targetDepth: 2);
        buffer.Push(Packet(0));
        buffer.Push(Packet(2)); // 1 never arrives
        Assert.True(buffer.TryDequeue(out _)); // 0

        // 2 and 3 are now queued behind the gap: enough evidence that 1 is lost, not late.
        buffer.Push(Packet(3));

        Assert.True(buffer.TryDequeue(out var concealed));
        Assert.Equal(PacketFlags.Silence, concealed.Flags);
        Assert.True(concealed.Payload.IsEmpty);
        Assert.Equal(1, buffer.ConcealedPacketCount);

        Assert.True(buffer.TryDequeue(out var resumed));
        Assert.Equal(2u, resumed.Sequence);
    }

    [Fact]
    public void UnderrunsRatherThanConcealingTooEagerly()
    {
        // Only one packet behind the gap: it may still be in flight, so wait.
        var buffer = new JitterBuffer(targetDepth: 3);
        buffer.Push(Packet(0));
        buffer.Push(Packet(1));
        buffer.Push(Packet(2));
        for (var i = 0; i < 3; i++) Assert.True(buffer.TryDequeue(out _));

        buffer.Push(Packet(4)); // 3 missing
        Assert.False(buffer.TryDequeue(out _));
        Assert.Equal(0, buffer.ConcealedPacketCount);
    }

    [Fact]
    public void NeverGrowsPastCapacity()
    {
        var buffer = new JitterBuffer(targetDepth: 2, capacity: 8);
        // A sender that runs while nothing drains must not exhaust memory.
        for (uint sequence = 0; sequence < 1000; sequence++)
            buffer.Push(Packet(sequence));

        Assert.True(buffer.Count <= 8);
    }

    [Fact]
    public void ResetRePrimes()
    {
        var buffer = new JitterBuffer(targetDepth: 2);
        buffer.Push(Packet(0));
        buffer.Push(Packet(1));
        Assert.True(buffer.IsPrimed);

        buffer.Reset();
        Assert.False(buffer.IsPrimed);
        Assert.Equal(0, buffer.Count);
        Assert.False(buffer.TryDequeue(out _));
    }
}
