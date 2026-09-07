using System.Net;
using System.Net.Sockets;

namespace AudioBridge.Core;

/// <summary>
/// Chops PCM into datagrams and fires them at the peer.
///
/// Fire-and-forget by design: there is no retransmission, because a packet that arrives
/// late is worse than useless — its slot has already played. Loss is handled at the far
/// end by <see cref="JitterBuffer"/> concealment instead.
/// </summary>
public sealed class AudioSender : IDisposable
{
    private readonly Socket _socket;
    private readonly AudioFormat _format;
    private readonly byte[] _buffer = new byte[AudioPacket.HeaderSize + AudioPacket.MaxPayloadSize];
    private readonly Dictionary<StreamId, (uint Sequence, uint Timestamp)> _counters = new();
    private readonly Lock _gate = new();

    public AudioSender(IPEndPoint destination, AudioFormat format)
    {
        Destination = destination;
        _format = format;
        _socket = new Socket(destination.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
    }

    public IPEndPoint Destination { get; }
    public long PacketsSent { get; private set; }
    public long BytesSent { get; private set; }

    /// <summary>Largest payload that is both under the MTU and a whole number of frames.
    /// Splitting a frame across datagrams would swap the channels for the rest of the stream.</summary>
    public int MaxChunkSize => AudioPacket.MaxPayloadSize / _format.BytesPerFrame * _format.BytesPerFrame;

    /// <summary>Sends a block of PCM, split across as many datagrams as it needs.</summary>
    public void Send(StreamId stream, ReadOnlySpan<byte> pcm)
    {
        if (pcm.Length % _format.BytesPerFrame != 0)
            throw new ArgumentException("PCM block must be a whole number of frames.", nameof(pcm));

        lock (_gate)
        {
            while (!pcm.IsEmpty)
            {
                var chunk = pcm[..Math.Min(MaxChunkSize, pcm.Length)];
                SendChunk(stream, chunk);
                pcm = pcm[chunk.Length..];
            }
        }
    }

    private void SendChunk(StreamId stream, ReadOnlySpan<byte> chunk)
    {
        _counters.TryGetValue(stream, out var counter);

        var packet = new AudioPacket(stream, counter.Sequence, counter.Timestamp, PacketFlags.None, default);
        // Write the header, then the payload straight into the same buffer, so a chunk of
        // audio never gets copied into an intermediate array on the hot path.
        packet.WriteTo(_buffer);
        chunk.CopyTo(_buffer.AsSpan(AudioPacket.HeaderSize));
        var total = AudioPacket.HeaderSize + chunk.Length;

        try
        {
            _socket.SendTo(_buffer.AsSpan(0, total), SocketFlags.None, Destination);
            PacketsSent++;
            BytesSent += total;
        }
        catch (SocketException)
        {
            // The peer going away mid-stream is normal (sleep, cable out, app closed).
            // Discovery will notice and the UI will show it; dropping audio is the right
            // response here, not tearing down the capture thread.
        }

        _counters[stream] = (
            unchecked(counter.Sequence + 1),
            unchecked(counter.Timestamp + (uint)(chunk.Length / _format.BytesPerFrame)));
    }

    public void Dispose() => _socket.Dispose();
}
