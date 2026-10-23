using System.Net;

namespace NetPulse.Domain.PrivilegedOperations;

/// <summary>
/// Validates a proposed DNS server list before it is allowed anywhere near the elevated
/// helper. Every address must be a literal IP - never a hostname, never anything that could
/// be interpreted as a command-line flag or separator - and the list must be a sane size.
///
/// An EMPTY list is valid and means "clear any static DNS override and use the adapter's
/// automatic (DHCP-assigned) DNS servers" - this is what SetDNSServerSearchOrder does when
/// given an empty array, and it is the only way to roll back an adapter that had no static
/// DNS configured before a change (the common case). Rejecting empty here would make
/// SetAdapterDnsServersAction.RollbackAsync unable to restore that original state.
/// </summary>
public static class DnsServerAddressValidator
{
    private const int MaxServers = 4;

    public static bool TryValidate(IReadOnlyList<string>? addresses, out string error)
    {
        error = string.Empty;

        if (addresses is null)
        {
            error = "DNS server list cannot be null.";
            return false;
        }

        if (addresses.Count > MaxServers)
        {
            error = $"At most {MaxServers} DNS server addresses are supported.";
            return false;
        }

        foreach (var address in addresses)
        {
            if (!IPAddress.TryParse(address, out var parsed))
            {
                error = $"'{address}' is not a valid IP address literal.";
                return false;
            }

            if (IPAddress.IsLoopback(parsed) || parsed.Equals(IPAddress.Any) || parsed.Equals(IPAddress.IPv6Any))
            {
                error = $"'{address}' is not a usable DNS server address.";
                return false;
            }
        }

        return true;
    }
}
