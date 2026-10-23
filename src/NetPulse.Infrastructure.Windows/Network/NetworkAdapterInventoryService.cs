using System.Net.NetworkInformation;
using NetPulse.Application.Ports;
using NetPulse.Domain.Network;

namespace NetPulse.Infrastructure.Windows.Network;

/// <summary>
/// Reads adapter state from <see cref="NetworkInterface"/> only - no registry edits, no
/// NetAdapter cmdlet calls. This keeps inventory read-only so it can run without elevation
/// and without risk of this module itself modifying machine state.
/// </summary>
public sealed class NetworkAdapterInventoryService : INetworkAdapterInventoryService
{
    public Task<IReadOnlyList<NetworkAdapterInfo>> GetAdaptersAsync(CancellationToken cancellationToken)
    {
        var adapters = NetworkInterface.GetAllNetworkInterfaces()
            .Select(ToAdapterInfo)
            .ToList();

        return Task.FromResult<IReadOnlyList<NetworkAdapterInfo>>(adapters);
    }

    private static NetworkAdapterInfo ToAdapterInfo(NetworkInterface nic)
    {
        var props = nic.GetIPProperties();
        IPv4InterfaceStatistics? stats = null;
        try
        {
            stats = nic.GetIPv4Statistics();
        }
        catch (NetworkInformationException)
        {
            // Not all adapter types expose IPv4 statistics (e.g. some tunnel/loopback interfaces).
        }

        long speed;
        try
        {
            speed = nic.Speed;
        }
        catch (NetworkInformationException)
        {
            speed = -1;
        }

        return new NetworkAdapterInfo(
            Id: nic.Id,
            Name: nic.Name,
            Description: nic.Description,
            Kind: ClassifyKind(nic),
            IsUp: nic.OperationalStatus == OperationalStatus.Up,
            NegotiatedSpeedBitsPerSecond: speed,
            IPv4Addresses: props.UnicastAddresses
                .Where(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                .Select(a => a.Address.ToString())
                .ToArray(),
            IPv6Addresses: props.UnicastAddresses
                .Where(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
                .Select(a => a.Address.ToString())
                .ToArray(),
            DnsServers: props.DnsAddresses.Select(a => a.ToString()).ToArray(),
            Gateway: props.GatewayAddresses.Select(g => g.Address.ToString()).FirstOrDefault(),
            Mtu: SafeMtu(props),
            BytesSent: stats?.BytesSent ?? 0,
            BytesReceived: stats?.BytesReceived ?? 0);
    }

    private static int SafeMtu(IPInterfaceProperties props)
    {
        try
        {
            // Some interfaces (observed on the loopback pseudo-interface) report a negative
            // MTU rather than throwing - that is not a real value, so normalize it to the
            // same "unknown" marker (0) used when the query is unsupported outright.
            var mtu = props.GetIPv4Properties().Mtu;
            return mtu > 0 ? mtu : 0;
        }
        catch (NetworkInformationException)
        {
            return 0;
        }
    }

    private static AdapterKind ClassifyKind(NetworkInterface nic)
    {
        if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            return AdapterKind.Loopback;

        if (nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
            return AdapterKind.Wireless;

        if (nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet || nic.NetworkInterfaceType == NetworkInterfaceType.GigabitEthernet)
        {
            var descriptionLower = nic.Description.ToLowerInvariant();
            return descriptionLower.Contains("usb") ? AdapterKind.UsbEthernet : AdapterKind.Ethernet;
        }

        return AdapterKind.Other;
    }
}
