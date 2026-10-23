using NetPulse.Domain.Network;

namespace NetPulse.App.ViewModels;

/// <summary>Thin, read-only presentation wrapper - no behavior, just formatting for the grid.</summary>
public sealed class AdapterRowViewModel
{
    public AdapterRowViewModel(NetworkAdapterInfo info)
    {
        Id = info.Id;
        Name = info.Name;
        Description = info.Description;
        Kind = info.Kind.ToString();
        Status = info.IsUp ? "Up" : "Down";
        Speed = info.NegotiatedSpeedBitsPerSecond > 0
            ? $"{info.NegotiatedSpeedBitsPerSecond / 1_000_000} Mbps"
            : "Unknown";
        IPv4 = info.IPv4Addresses.Length > 0 ? string.Join(", ", info.IPv4Addresses) : "-";
        Gateway = info.Gateway ?? "-";
        Dns = info.DnsServers.Length > 0 ? string.Join(", ", info.DnsServers) : "-";
        Mtu = info.Mtu > 0 ? info.Mtu.ToString() : "-";
    }

    public string Id { get; }
    public string Name { get; }
    public string Description { get; }
    public string Kind { get; }
    public string Status { get; }
    public string Speed { get; }
    public string IPv4 { get; }
    public string Gateway { get; }
    public string Dns { get; }
    public string Mtu { get; }
}
