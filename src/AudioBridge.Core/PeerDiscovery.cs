using System.Net;
using System.Net.Sockets;

namespace AudioBridge.Core;

/// <summary>
/// Finds the other PC with no configuration: broadcast a beacon once a second, listen for
/// everyone else's, expire the ones that go quiet.
///
/// Plain UDP broadcast rather than mDNS on purpose — it is a handful of lines with no
/// dependency, and both PCs are on the same LAN by definition here. The cost is that it
/// does not cross subnets, which is why the UI also allows entering an address by hand.
/// </summary>
public sealed class PeerDiscovery : IAsyncDisposable
{
    /// <summary>Fixed port so both ends find each other without configuration.</summary>
    public const int DiscoveryPort = 47812;

    private readonly Socket _socket;
    private readonly PeerTable _table;
    private readonly TimeSpan _interval;
    private readonly Guid _instanceId;
    private readonly Lock _gate = new();
    private CancellationTokenSource? _cancellation;
    private Task? _listenLoop;
    private Task? _announceLoop;

    public PeerDiscovery(Guid instanceId, string machineName, ushort audioPort, PeerRole role, TimeSpan? interval = null)
    {
        _instanceId = instanceId;
        MachineName = machineName;
        AudioPort = audioPort;
        Role = role;
        _interval = interval ?? TimeSpan.FromSeconds(2);
        // A peer is gone after it misses several beacons; one dropped broadcast is normal.
        _table = new PeerTable(instanceId, _interval * 5);

        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        // Both PCs may run other AudioBridge-aware tools, and on the same machine two
        // instances must not fight over the port during development.
        _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _socket.EnableBroadcast = true;
        NetworkTargets.IgnoreConnectionReset(_socket);
        _socket.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
    }

    public string MachineName { get; }
    public ushort AudioPort { get; }

    /// <summary>Advertised role. Settable because the user picks it in the wizard after start-up.</summary>
    public PeerRole Role { get; set; }

    public IReadOnlyCollection<Peer> Peers
    {
        get { lock (_gate) return _table.Peers; }
    }

    public event Action<Peer>? PeerDiscovered;
    public event Action<Peer>? PeerLost;

    public void Start()
    {
        if (_cancellation is not null) throw new InvalidOperationException("Already started.");
        _cancellation = new CancellationTokenSource();
        _listenLoop = Task.Run(() => ListenAsync(_cancellation.Token));
        _announceLoop = Task.Run(() => AnnounceAsync(_cancellation.Token));
    }

    private async Task AnnounceAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_interval);
        do
        {
            var payload = new DiscoveryBeacon(_instanceId, MachineName, AudioPort, Role).ToArray();

            // Recomputed every tick so adapters appearing or disappearing (VPN connecting,
            // cable plugged in) are picked up without a restart.
            foreach (var target in NetworkTargets.BroadcastTargets())
            {
                try
                {
                    _socket.SendTo(payload, new IPEndPoint(target, DiscoveryPort));
                }
                catch (SocketException)
                {
                    // One adapter refusing is normal; keep trying the others.
                }
                catch (ObjectDisposedException) { return; }
            }

            ExpireQuietPeers();
        }
        while (await SafeWaitAsync(timer, cancellationToken));
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[512];
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
            catch (SocketException) { continue; }

            if (!DiscoveryBeacon.TryParse(buffer.AsSpan(0, result.ReceivedBytes), out var beacon))
                continue;

            Peer? discovered;
            lock (_gate)
                discovered = _table.Observe(beacon, ((IPEndPoint)result.RemoteEndPoint).Address, DateTimeOffset.UtcNow);

            if (discovered is not null) PeerDiscovered?.Invoke(discovered);
        }
    }

    private void ExpireQuietPeers()
    {
        IReadOnlyList<Peer> dropped;
        lock (_gate) dropped = _table.Prune(DateTimeOffset.UtcNow);
        foreach (var peer in dropped) PeerLost?.Invoke(peer);
    }

    private static async Task<bool> SafeWaitAsync(PeriodicTimer timer, CancellationToken cancellationToken)
    {
        try { return await timer.WaitForNextTickAsync(cancellationToken); }
        catch (OperationCanceledException) { return false; }
    }

    public async ValueTask DisposeAsync()
    {
        if (_cancellation is not null) await _cancellation.CancelAsync();
        _socket.Dispose();
        foreach (var task in new[] { _listenLoop, _announceLoop })
        {
            if (task is null) continue;
            try { await task; } catch (OperationCanceledException) { }
        }
        _cancellation?.Dispose();
    }
}
