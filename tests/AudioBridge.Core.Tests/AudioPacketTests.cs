using AudioBridge.Core;

namespace AudioBridge.Core.Tests;

public class AudioPacketTests
{
    [Fact]
    public void RoundTripsThroughTheWireFormat()
    {
        var payload = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var original = new AudioPacket(StreamId.Microphone, 42, 4800, PacketFlags.Opus, payload);

        Assert.True(AudioPacket.TryParse(original.ToArray(), out var parsed));
        Assert.Equal(StreamId.Microphone, parsed.Stream);
        Assert.Equal(42u, parsed.Sequence);
        Assert.Equal(4800u, parsed.Timestamp);
        Assert.Equal(PacketFlags.Opus, parsed.Flags);
        Assert.Equal(payload, parsed.Payload.ToArray());
    }

    [Fact]
    public void HeaderIsSixteenBytes()
    {
        var packet = new AudioPacket(StreamId.GameAudio, 0, 0, PacketFlags.None, new byte[100]);
        Assert.Equal(116, packet.ToArray().Length);
    }

    [Theory]
    [InlineData(0)]           // too short
    [InlineData(8)]           // still short of a header
    public void RejectsTruncatedInput(int length) =>
        Assert.False(AudioPacket.TryParse(new byte[length], out _));

    [Fact]
    public void RejectsForeignTraffic()
    {
        // The socket is open to the LAN; stray packets must be ignored, not throw.
        var noise = new byte[64];
        Random.Shared.NextBytes(noise);
        noise[0] = 0xFF; // definitely not our magic
        Assert.False(AudioPacket.TryParse(noise, out _));
    }

    [Fact]
    public void RejectsUnknownVersion()
    {
        var bytes = new AudioPacket(StreamId.GameAudio, 1, 1, PacketFlags.None, new byte[4]).ToArray();
        bytes[4] = 99;
        Assert.False(AudioPacket.TryParse(bytes, out _));
    }

    [Fact]
    public void SequenceComparisonSurvivesWraparound()
    {
        Assert.True(AudioPacket.IsNewer(5, 4));
        Assert.False(AudioPacket.IsNewer(4, 5));
        Assert.False(AudioPacket.IsNewer(5, 5));
        // The case that matters: 0 follows uint.MaxValue.
        Assert.True(AudioPacket.IsNewer(0, uint.MaxValue));
        Assert.False(AudioPacket.IsNewer(uint.MaxValue, 0));
    }
}
