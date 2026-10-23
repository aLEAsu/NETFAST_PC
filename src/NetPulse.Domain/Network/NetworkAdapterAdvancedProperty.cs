namespace NetPulse.Domain.Network;

/// <summary>
/// One advanced driver/hardware property of a network adapter - the same data
/// <c>Get-NetAdapterAdvancedProperty</c> shows (RSS, offloads, Energy Efficient Ethernet,
/// jumbo packet size, etc.). Read-only by construction: this record has no setter and
/// nothing in this build ever writes one of these back to the adapter - see Module C's
/// scope note on <see cref="NetPulse.Application.Ports.IAdapterAdvancedPropertiesReader"/>.
/// </summary>
public sealed record NetworkAdapterAdvancedProperty(
    string Keyword,
    string DisplayName,
    string DisplayValue,
    string RawValue);
