using System.Net;
using System.Net.Sockets;

namespace AudioBridge.Core;

/// <summary>
/// Listens for audio datagrams and files them into a jitter buffer per stream.
///
/// The receive loop only ever enqueues; the audio device's render callback drains via
/// <see cref="TryRead"/>. Keeping the socket off the audio thread is the whole point —
/// a blocking receive inside a render callback is what causes crackling.
/// </summary>
public sealed class AudioReceiver : IAsyncDisposable
{
    private readonly Socket _socket;
    private readonly Dictionary<StreamId, JitterBuffer> _buffers = new();
    private readonly Lock _gate = new();
    private readonly int _jitterDepth;
    private CancellationTokenSource? _cancellation;
    private Task? _loop;

    /// <param name="port">Port to listen on; 0 picks a free one, readable from <see cref="Port"/>.</param>
    /// <param name="jitterDepth">Packets to hold back before playback starts.</param>
    public AudioReceiver(int port = 0, int jitterDepth = 3)
    {
        _jitterDepth = jitterDepth;
        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        _socket.Bind(new IPEndPoint(IPAddress.Any, port));
    }

    public int Port => ((IPEndPoint)_socket.LocalEndPoint!).Port;
    public long PacketsReceived { get; private set; }
    public long PacketsRejected { get; private set; }

    /// <summary>Where the most recent valid packet came from. Lets the receiving side reply
    /// without the user configuring anything.</summary>
    public IPEndPoint? LastSender { get; private set; }

    public void Start()
    {
        if (_loop is not null) throw new InvalidOperationException("Already started.");
        _cancellation = new CancellationTokenSource();
        _loop = Task.Run(() => ReceiveLoopAsync(_cancellation.Token));
    }

    /// <summary>Pulls the next packet for a stream. Returns false on underrun, which the
    /// caller should treat as "render silence and come back next callback".</summary>
    public bool TryRead(StreamId stream, out AudioPacket packet)
    {
        lock (_gate)
        {
            if (_buffers.TryGetValue(stream, out var buffer))
                return buffer.TryDequeue(out packet);
        }
        packet = default;
        return false;
    }

    public JitterBufferStats GetStats(StreamId stream)
    {
        lock (_gate)
        {
            return _buffers.TryGetValue(stream, out var buffer)
                ? new JitterBufferStats(buffer.Count, buffer.IsPrimed, buffer.LatePacketCount, buffer.ConcealedPacketCount)
                : new JitterBufferStats(0, false, 0, 0);
        }
    }

    /// <summary>Drops buffered audio for a stream, e.g. when the sender restarts.</summary>
    public void ResetStream(StreamId stream)
    {
        lock (_gate)
        {
            if (_buffers.TryGetValue(stream, out var buffer)) buffer.Reset();
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[AudioPacket.HeaderSize + AudioPacket.MaxPayloadSize];
        var remote = new IPEndPoint(IPAddress.Any, 0);

        while (!cancellationToken.IsCancellationRequested)
        {
            SocketReceiveFromResult result;
            try
            {
                result = await _socket.ReceiveFromAsync(buffer, SocketFlags.None, remote, cancellationToken);
            }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }
            catch (SocketException)
            {
                // On Windows an ICMP "port unreachable" from a peer that closed can surface
                // here as an error on an otherwise healthy socket. Keep listening.
                continue;
            }

            if (!AudioPacket.TryParse(buffer.AsSpan(0, result.ReceivedBytes), out var packet))
            {
                PacketsRejected++;
                continue;
            }

            PacketsReceived++;
            LastSender = (IPEndPoint)result.RemoteEndPoint;

            lock (_gate)
            {
                if (!_buffers.TryGetValue(packet.Stream, out var jitter))
                    _buffers[packet.Stream] = jitter = new JitterBuffer(_jitterDepth);
                jitter.Push(packet);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_cancellation is not null) await _cancellation.CancelAsync();
        _socket.Dispose();
        if (_loop is not null)
        {
            try { await _loop; } catch (OperationCanceledException) { }
        }
        _cancellation?.Dispose();
    }
}

public readonly record struct JitterBufferStats(int Depth, bool IsPrimed, int LatePackets, int ConcealedPackets);
