using System.Net;
using AudioBridge.Core;

namespace AudioBridge.Core.Tests;

public class NetworkTargetsTests
{
    [Fact]
    public void AlwaysIncludesTheLimitedBroadcast()
    {
        // Even with no usable adapters we must still announce somewhere.
        Assert.Contains(IPAddress.Broadcast, NetworkTargets.BroadcastTargets());
    }

    [Fact]
    public void IncludesADirectedBroadcastForEachSubnet()
    {
        // The bug this guards: sending only to 255.255.255.255 leaves via one adapter, so on
        // a PC with Wi-Fi plus Ethernet the beacon often goes out the wrong one.
        var targets = NetworkTargets.BroadcastTargets();
        var localSubnets = NetworkTargets.LocalAddresses().Count;

        if (localSubnets > 0)
            Assert.True(targets.Count > 1, "Expected at least one directed broadcast alongside 255.255.255.255.");
    }

    [Theory]
    // The ordinary home-network case.
    [InlineData("192.168.1.5", "255.255.255.0", "192.168.1.255")]
    [InlineData("192.168.10.81", "255.255.255.0", "192.168.10.255")]
    // Larger subnets, which some routers and VPNs hand out.
    [InlineData("10.0.3.7", "255.0.0.0", "10.255.255.255")]
    [InlineData("172.16.4.9", "255.255.0.0", "172.16.255.255")]
    [InlineData("192.168.1.66", "255.255.255.192", "192.168.1.127")]
    // A point-to-point /32 has no host bits, so it broadcasts only to itself.
    [InlineData("10.2.0.2", "255.255.255.255", "10.2.0.2")]
    public void ComputesTheSubnetBroadcastAddress(string address, string mask, string expected) =>
        Assert.Equal(
            IPAddress.Parse(expected),
            NetworkTargets.DirectedBroadcast(IPAddress.Parse(address), IPAddress.Parse(mask)));

    [Fact]
    public void NeverReturnsDuplicates()
    {
        var targets = NetworkTargets.BroadcastTargets();
        Assert.Equal(targets.Count, targets.Distinct().Count());
    }

    [Fact]
    public void LocalAddressesExcludeLoopback() =>
        Assert.DoesNotContain(IPAddress.Loopback, NetworkTargets.LocalAddresses());

    [Fact]
    public void IgnoringConnectionResetIsSafeOnAnyPlatform()
    {
        // The ioctl is Windows-only; on macOS/Linux it must be a no-op, not a throw.
        using var socket = new System.Net.Sockets.Socket(
            System.Net.Sockets.AddressFamily.InterNetwork,
            System.Net.Sockets.SocketType.Dgram,
            System.Net.Sockets.ProtocolType.Udp);
        NetworkTargets.IgnoreConnectionReset(socket);
    }
}
