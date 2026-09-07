using System.Net;
using AudioBridge.Core;

namespace AudioBridge.Core.Tests;

public class DiscoveryBeaconTests
{
    [Fact]
    public void RoundTrips()
    {
        var id = Guid.NewGuid();
        var original = new DiscoveryBeacon(id, "GAMING-RIG", 47810, PeerRole.GamingPc);

        Assert.True(DiscoveryBeacon.TryParse(original.ToArray(), out var parsed));
        Assert.Equal(id, parsed.InstanceId);
        Assert.Equal("GAMING-RIG", parsed.MachineName);
        Assert.Equal(47810, parsed.AudioPort);
        Assert.Equal(PeerRole.GamingPc, parsed.Role);
    }

    [Fact]
    public void HandlesNonAsciiMachineNames()
    {
        var original = new DiscoveryBeacon(Guid.NewGuid(), "Andrés-PC ✨", 47810, PeerRole.StreamingPc);
        Assert.True(DiscoveryBeacon.TryParse(original.ToArray(), out var parsed));
        Assert.Equal("Andrés-PC ✨", parsed.MachineName);
    }

    [Fact]
    public void TruncatesAbsurdlyLongNamesRatherThanThrowing()
    {
        var original = new DiscoveryBeacon(Guid.NewGuid(), new string('x', 500), 47810, PeerRole.GamingPc);
        Assert.True(DiscoveryBeacon.TryParse(original.ToArray(), out var parsed));
        Assert.True(parsed.MachineName.Length is > 0 and <= 120);
    }

    [Fact]
    public void RejectsForeignBroadcastTraffic()
    {
        // The discovery port sees every broadcast on the LAN; none of it may throw.
        var noise = new byte[200];
        Random.Shared.NextBytes(noise);
        noise[0] = 0x00;
        Assert.False(DiscoveryBeacon.TryParse(noise, out _));
        Assert.False(DiscoveryBeacon.TryParse([], out _));
    }

    [Fact]
    public void RejectsBeaconTruncatedMidName()
    {
        var bytes = new DiscoveryBeacon(Guid.NewGuid(), "LONG-MACHINE-NAME", 1, PeerRole.GamingPc).ToArray();
        Assert.False(DiscoveryBeacon.TryParse(bytes.AsSpan(0, bytes.Length - 4), out _));
    }
}

public class PeerTableTests
{
    private static readonly IPAddress Source = IPAddress.Parse("192.168.1.50");
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static DiscoveryBeacon Beacon(Guid id, PeerRole role = PeerRole.GamingPc) =>
        new(id, "PEER", 47810, role);

    [Fact]
    public void ReportsAPeerOnceAndThenStaysQuiet()
    {
        var table = new PeerTable(Guid.NewGuid());
        var peerId = Guid.NewGuid();

        Assert.NotNull(table.Observe(Beacon(peerId), Source, T0));
        Assert.Null(table.Observe(Beacon(peerId), Source, T0.AddSeconds(1)));
        Assert.Single(table.Peers);
    }

    [Fact]
    public void IgnoresOurOwnBroadcasts()
    {
        var self = Guid.NewGuid();
        var table = new PeerTable(self);
        Assert.Null(table.Observe(Beacon(self), Source, T0));
        Assert.Empty(table.Peers);
    }

    [Fact]
    public void ReportsAgainWhenAPeerChangesRole()
    {
        // The user picking "this is my gaming PC" in the wizard must reach the other end.
        var table = new PeerTable(Guid.NewGuid());
        var peerId = Guid.NewGuid();
        table.Observe(Beacon(peerId, PeerRole.Unconfigured), Source, T0);

        var changed = table.Observe(Beacon(peerId, PeerRole.GamingPc), Source, T0.AddSeconds(1));
        Assert.NotNull(changed);
        Assert.Equal(PeerRole.GamingPc, changed.Role);
        Assert.Single(table.Peers);
    }

    [Fact]
    public void ReportsAgainWhenAPeerChangesAddress()
    {
        var table = new PeerTable(Guid.NewGuid());
        var peerId = Guid.NewGuid();
        table.Observe(Beacon(peerId), Source, T0);

        var moved = table.Observe(Beacon(peerId), IPAddress.Parse("192.168.1.77"), T0.AddSeconds(1));
        Assert.NotNull(moved);
        Assert.Equal("192.168.1.77", moved.AudioEndPoint.Address.ToString());
    }

    [Fact]
    public void DropsPeersThatGoQuiet()
    {
        var table = new PeerTable(Guid.NewGuid(), TimeSpan.FromSeconds(5));
        var peerId = Guid.NewGuid();
        table.Observe(Beacon(peerId), Source, T0);

        Assert.Empty(table.Prune(T0.AddSeconds(4)));

        var dropped = table.Prune(T0.AddSeconds(6));
        Assert.Equal(peerId, Assert.Single(dropped).InstanceId);
        Assert.Empty(table.Peers);
    }

    [Fact]
    public void KeepsPeersAliveWhileBeaconsContinue()
    {
        var table = new PeerTable(Guid.NewGuid(), TimeSpan.FromSeconds(5));
        var peerId = Guid.NewGuid();

        for (var second = 0; second < 60; second++)
        {
            table.Observe(Beacon(peerId), Source, T0.AddSeconds(second));
            Assert.Empty(table.Prune(T0.AddSeconds(second)));
        }
        Assert.Single(table.Peers);
    }
}
