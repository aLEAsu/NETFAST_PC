using NetPulse.Domain.Network;

namespace NetPulse.App.ViewModels;

/// <summary>Thin, read-only presentation wrapper - no behavior, just formatting for the grid.</summary>
public sealed class AdapterRowViewModel
{
    public AdapterRowViewModel(NetworkAdapterInfo info)
    {
        Id = info.Id;
        IsVirtual = info.IsVirtual;
        Name = info.Name;
        Description = info.Description;
        Kind = TranslateKind(info.Kind);
        Status = info.IsUp ? "Activo" : "Inactivo";
        Speed = info.NegotiatedSpeedBitsPerSecond > 0
            ? $"{info.NegotiatedSpeedBitsPerSecond / 1_000_000} Mbps"
            : "Desconocida";
        IPv4 = info.IPv4Addresses.Length > 0 ? string.Join(", ", info.IPv4Addresses) : "-";
        Gateway = info.Gateway ?? "-";
        Dns = info.DnsServers.Length > 0 ? string.Join(", ", info.DnsServers) : "-";
        Mtu = info.Mtu > 0 ? info.Mtu.ToString() : "-";
    }

    public string Id { get; }

    /// <summary>True for Windows' built-in virtual/tunnel adapters (WAN Miniport, 6to4,
    /// Teredo, ISATAP, loopback) - never a real NIC. Drives the "show virtual adapters"
    /// filter in the dashboard; not shown as its own grid column.</summary>
    public bool IsVirtual { get; }

    public string Name { get; }
    public string Description { get; }
    public string Kind { get; }
    public string Status { get; }
    public string Speed { get; }
    public string IPv4 { get; }
    public string Gateway { get; }
    public string Dns { get; }
    public string Mtu { get; }

    private static string TranslateKind(AdapterKind kind) => kind switch
    {
        AdapterKind.Ethernet => "Ethernet",
        AdapterKind.Wireless => "Wi-Fi",
        AdapterKind.UsbEthernet => "Ethernet por USB",
        AdapterKind.Loopback => "Loopback",
        AdapterKind.Tunnel => "Túnel (virtual)",
        AdapterKind.Other => "Otro",
        _ => kind.ToString(),
    };
}
