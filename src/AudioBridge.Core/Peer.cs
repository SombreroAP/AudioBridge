using System.Net;

namespace AudioBridge.Core;

/// <summary>Another AudioBridge instance seen on the network.</summary>
public sealed record Peer(Guid InstanceId, string MachineName, IPEndPoint AudioEndPoint, PeerRole Role)
{
    public override string ToString() => $"{MachineName} ({Role}) at {AudioEndPoint}";
}

/// <summary>
/// Tracks which peers are currently alive, based on beacons arriving and then stopping.
///
/// Split out from the socket code so the add/refresh/expire logic can be tested without
/// touching the network or waiting on real clocks — every method takes the current time.
/// </summary>
public sealed class PeerTable
{
    private readonly Dictionary<Guid, (Peer Peer, DateTimeOffset LastSeen)> _peers = new();
    private readonly TimeSpan _timeout;
    private readonly Guid _selfInstanceId;

    /// <param name="selfInstanceId">Our own id, so we ignore our own broadcasts.</param>
    /// <param name="timeout">How long without a beacon before a peer is considered gone.</param>
    public PeerTable(Guid selfInstanceId, TimeSpan? timeout = null)
    {
        _selfInstanceId = selfInstanceId;
        _timeout = timeout ?? TimeSpan.FromSeconds(5);
    }

    public IReadOnlyCollection<Peer> Peers => _peers.Values.Select(entry => entry.Peer).ToList();

    /// <summary>Records a beacon. Returns the peer when it is newly discovered or its
    /// details changed, and null when it is merely a keepalive for a peer we already know.</summary>
    public Peer? Observe(in DiscoveryBeacon beacon, IPAddress source, DateTimeOffset now)
    {
        if (beacon.InstanceId == _selfInstanceId) return null;

        var peer = new Peer(
            beacon.InstanceId,
            beacon.MachineName,
            new IPEndPoint(source, beacon.AudioPort),
            beacon.Role);

        var isNew = !_peers.TryGetValue(beacon.InstanceId, out var existing) || existing.Peer != peer;
        _peers[beacon.InstanceId] = (peer, now);
        return isNew ? peer : null;
    }

    /// <summary>Removes peers that have gone quiet. Returns those that were dropped.</summary>
    public IReadOnlyList<Peer> Prune(DateTimeOffset now)
    {
        List<Peer>? dropped = null;
        foreach (var (id, entry) in _peers.ToList())
        {
            if (now - entry.LastSeen <= _timeout) continue;
            _peers.Remove(id);
            (dropped ??= []).Add(entry.Peer);
        }
        return (IReadOnlyList<Peer>?)dropped ?? [];
    }

    public void Clear() => _peers.Clear();
}
