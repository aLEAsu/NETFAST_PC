namespace NetPulse.Domain.Network;

public enum AdapterKind
{
    Ethernet,
    Wireless,
    UsbEthernet,
    Loopback,
    /// <summary>Tunneling pseudo-adapters (6to4, Teredo, ISATAP) - never a real NIC.</summary>
    Tunnel,
    Other
}

public sealed record NetworkAdapterInfo(
    string Id,
    string Name,
    string Description,
    AdapterKind Kind,
    bool IsUp,
    long NegotiatedSpeedBitsPerSecond,
    string[] IPv4Addresses,
    string[] IPv6Addresses,
    string[] DnsServers,
    string? Gateway,
    int Mtu,
    long BytesSent,
    long BytesReceived,
    /// <summary>
    /// True for adapters that don't correspond to real, physical network hardware - tunnel
    /// pseudo-interfaces (6to4/Teredo/ISATAP), the loopback interface, and the "WAN
    /// Miniport" virtual adapters Windows' Remote Access service always installs for VPN
    /// protocols (PPTP/L2TP/SSTP/IKEv2/IP/IPv6/Network Monitor) whether or not the user has
    /// ever used a VPN. Windows exposes these through the exact same API as real NICs, so
    /// NetworkInterface.GetAllNetworkInterfaces() on a machine with one real Wi-Fi adapter
    /// commonly returns 8-10 entries - this flag is what lets the UI default to hiding the
    /// ones that are never useful for diagnosing an actual network problem.
    /// </summary>
    bool IsVirtual);
