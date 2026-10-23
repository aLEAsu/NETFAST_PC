namespace NetPulse.Domain.Network;

public enum AdapterKind
{
    Ethernet,
    Wireless,
    UsbEthernet,
    Loopback,
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
    long BytesReceived);
