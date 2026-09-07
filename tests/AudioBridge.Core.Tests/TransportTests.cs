using System.Diagnostics;
using System.Net;
using AudioBridge.Core;

namespace AudioBridge.Core.Tests;

/// <summary>Real UDP over the loopback adapter. Slower than the unit tests but this is the
/// path that actually has to work between two PCs.</summary>
public class TransportTests
{
    private static readonly AudioFormat Format = AudioFormat.Default;

    /// <summary>Waits for a condition rather than sleeping a fixed time, so the tests stay
    /// fast locally and don't flake on a loaded CI machine.</summary>
    private static async Task<bool> WaitUntilAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.ElapsedMilliseconds < timeoutMs)
        {
            if (condition()) return true;
            await Task.Delay(10);
        }
        return condition();
    }

    [Fact]
    public async Task DeliversAudioInOrderOverTheWire()
    {
        await using var receiver = new AudioReceiver(port: 0, jitterDepth: 2);
        receiver.Start();

        using var sender = new AudioSender(new IPEndPoint(IPAddress.Loopback, receiver.Port), Format);

        // 10 blocks of 5 ms, each filled with a distinct byte so we can verify ordering.
        // 5 ms of stereo is 960 bytes, which fits one datagram, so packet N is block N.
        var blockSize = Format.BytesForDuration(5);
        Assert.True(blockSize <= sender.MaxChunkSize);
        for (byte block = 0; block < 10; block++)
        {
            var pcm = new byte[blockSize];
            Array.Fill(pcm, block);
            sender.Send(StreamId.GameAudio, pcm);
        }

        Assert.True(await WaitUntilAsync(() => receiver.PacketsReceived >= 10),
            $"Only received {receiver.PacketsReceived} packets.");

        for (byte expected = 0; expected < 10; expected++)
        {
            Assert.True(receiver.TryRead(StreamId.GameAudio, out var packet), $"No packet for block {expected}.");
            Assert.Equal(expected, packet.Payload.Span[0]);
        }
    }

    [Fact]
    public async Task KeepsTheTwoDirectionsSeparate()
    {
        await using var receiver = new AudioReceiver(port: 0, jitterDepth: 1);
        receiver.Start();
        using var sender = new AudioSender(new IPEndPoint(IPAddress.Loopback, receiver.Port), Format);

        var game = new byte[Format.BytesForDuration(10)];
        Array.Fill(game, (byte)0xAA);
        var mic = new byte[Format.BytesForDuration(10)];
        Array.Fill(mic, (byte)0xBB);

        sender.Send(StreamId.GameAudio, game);
        sender.Send(StreamId.Microphone, mic);

        Assert.True(await WaitUntilAsync(() => receiver.PacketsReceived >= 2));

        Assert.True(receiver.TryRead(StreamId.GameAudio, out var gamePacket));
        Assert.Equal(0xAA, gamePacket.Payload.Span[0]);
        Assert.True(receiver.TryRead(StreamId.Microphone, out var micPacket));
        Assert.Equal(0xBB, micPacket.Payload.Span[0]);
    }

    [Fact]
    public async Task SplitsBlocksTooLargeForOneDatagram()
    {
        await using var receiver = new AudioReceiver(port: 0, jitterDepth: 1);
        receiver.Start();
        using var sender = new AudioSender(new IPEndPoint(IPAddress.Loopback, receiver.Port), Format);

        // 100 ms of 48 kHz stereo is ~19 KB — well over one MTU.
        var pcm = new byte[Format.BytesForDuration(100)];
        var expectedPackets = (int)Math.Ceiling((double)pcm.Length / sender.MaxChunkSize);
        Assert.True(expectedPackets > 1, "Test needs a block that spans several datagrams.");

        sender.Send(StreamId.GameAudio, pcm);
        Assert.True(await WaitUntilAsync(() => receiver.PacketsReceived >= expectedPackets));
        Assert.Equal(expectedPackets, sender.PacketsSent);
    }

    [Fact]
    public void ChunksNeverSplitAFrame() =>
        Assert.Equal(0, new AudioSender(new IPEndPoint(IPAddress.Loopback, 1), Format).MaxChunkSize % Format.BytesPerFrame);

    [Fact]
    public void RejectsPcmThatIsNotAWholeNumberOfFrames()
    {
        using var sender = new AudioSender(new IPEndPoint(IPAddress.Loopback, 1), Format);
        Assert.Throws<ArgumentException>(() => sender.Send(StreamId.GameAudio, new byte[7]));
    }

    [Fact]
    public async Task SendingToNobodyDoesNotThrow()
    {
        // A peer that sleeps or closes the app must not take down the capture thread.
        using var sender = new AudioSender(new IPEndPoint(IPAddress.Loopback, 1), Format);
        var pcm = new byte[Format.BytesForDuration(10)];
        for (var i = 0; i < 20; i++) sender.Send(StreamId.GameAudio, pcm);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task IgnoresGarbageArrivingOnTheAudioPort()
    {
        await using var receiver = new AudioReceiver(port: 0, jitterDepth: 1);
        receiver.Start();

        using var raw = new System.Net.Sockets.UdpClient();
        var noise = new byte[64];
        Random.Shared.NextBytes(noise);
        noise[0] = 0xFF;
        await raw.SendAsync(noise, noise.Length, new IPEndPoint(IPAddress.Loopback, receiver.Port));

        Assert.True(await WaitUntilAsync(() => receiver.PacketsRejected >= 1));
        Assert.Equal(0, receiver.PacketsReceived);
        Assert.False(receiver.TryRead(StreamId.GameAudio, out _));
    }

    [Fact]
    public async Task RecordsWhereAudioCameFromSoTheReplyNeedsNoConfiguration()
    {
        await using var receiver = new AudioReceiver(port: 0, jitterDepth: 1);
        receiver.Start();
        using var sender = new AudioSender(new IPEndPoint(IPAddress.Loopback, receiver.Port), Format);

        sender.Send(StreamId.GameAudio, new byte[Format.BytesForDuration(10)]);
        Assert.True(await WaitUntilAsync(() => receiver.LastSender is not null));
        Assert.Equal(IPAddress.Loopback, receiver.LastSender!.Address);
    }
}
