using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace AudioBridge.Core;

/// <summary>Networking odds and ends shared by discovery and the audio sockets.</summary>
public static class NetworkTargets
{
    /// <summary>SIO_UDP_CONNRESET. Windows-only ioctl.</summary>
    private const int UdpConnectionReset = -1744830452;

    /// <summary>
    /// Every address a beacon should be sent to.
    ///
    /// 255.255.255.255 is not enough on its own: Windows sends a limited broadcast out of
    /// only one adapter, so on a PC with several (Wi-Fi plus Ethernet, a VM switch, a VPN)
    /// the beacon frequently leaves on the wrong one and the other PC never hears it. Adding
    /// each interface's directed subnet broadcast — 192.168.1.255 and friends — makes sure
    /// the beacon reaches the network the other PC is actually on.
    /// </summary>
    public static IReadOnlyCollection<IPAddress> BroadcastTargets()
    {
        var targets = new HashSet<IPAddress> { IPAddress.Broadcast };
        try
        {
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.OperationalStatus != OperationalStatus.Up) continue;
                if (adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                foreach (var unicast in adapter.GetIPProperties().UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    if (unicast.IPv4Mask is null) continue;

                    targets.Add(DirectedBroadcast(unicast.Address, unicast.IPv4Mask));
                }
            }
        }
        catch (NetworkInformationException)
        {
            // Enumerating adapters can fail while the network is being reconfigured.
            // The limited broadcast alone is better than not announcing at all.
        }
        return targets;
    }

    /// <summary>The subnet broadcast address for an interface: the address with every host
    /// bit set. For 192.168.1.5/255.255.255.0 that is 192.168.1.255.</summary>
    public static IPAddress DirectedBroadcast(IPAddress address, IPAddress mask)
    {
        var addressBytes = address.GetAddressBytes();
        var maskBytes = mask.GetAddressBytes();
        if (addressBytes.Length != 4 || maskBytes.Length != 4)
            throw new ArgumentException("Directed broadcast is only defined for IPv4.");

        var broadcast = new byte[4];
        for (var i = 0; i < 4; i++) broadcast[i] = (byte)(addressBytes[i] | ~maskBytes[i]);
        return new IPAddress(broadcast);
    }

    /// <summary>This PC's own IPv4 addresses, for showing the user what to type on the other PC.</summary>
    public static IReadOnlyList<IPAddress> LocalAddresses()
    {
        var addresses = new List<IPAddress>();
        try
        {
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.OperationalStatus != OperationalStatus.Up) continue;
                if (adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                foreach (var unicast in adapter.GetIPProperties().UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily == AddressFamily.InterNetwork)
                        addresses.Add(unicast.Address);
                }
            }
        }
        catch (NetworkInformationException) { }
        return addresses;
    }

    /// <summary>
    /// Stops Windows turning an ICMP "port unreachable" from a peer into an error on our
    /// own socket. Without this, the moment the other PC closes AudioBridge our receive loop
    /// starts throwing, even though the socket is perfectly healthy.
    /// </summary>
    public static void IgnoreConnectionReset(Socket socket)
    {
        try
        {
            socket.IOControl(unchecked((IOControlCode)UdpConnectionReset), [0, 0, 0, 0], null);
        }
        catch (Exception)
        {
            // Not supported off Windows, and harmless there.
        }
    }
}
