using System.Management;
using NetPulse.Application.Ports;
using NetPulse.Domain.PrivilegedOperations;

namespace NetPulse.Infrastructure.Windows.Network;

/// <summary>
/// Read-only WMI query against Win32_NetworkAdapterConfiguration. This class has no method
/// that writes anything - the write path (SetDNSServerSearchOrder) exists only inside
/// NetPulse.PrivilegedAgent.Host, never here, so this project (loaded into the unelevated
/// app process) cannot change DNS configuration even if a caller tried to misuse it.
/// </summary>
public sealed class WmiAdapterDnsConfigurationReader : IAdapterDnsConfigurationReader
{
    public Task<IReadOnlyList<string>> GetDnsServersAsync(string adapterId, CancellationToken cancellationToken)
    {
        if (!AdapterIdValidator.TryValidate(adapterId, out var error))
            throw new ArgumentException(error, nameof(adapterId));

        // The WMI call below is blocking (System.Management has no async API) - running it
        // on the thread pool rather than returning a pre-completed Task keeps a future WPF
        // caller's UI thread free while it runs.
        return Task.Run(() => QueryDnsServers(adapterId, cancellationToken), cancellationToken);
    }

    private static IReadOnlyList<string> QueryDnsServers(string adapterId, CancellationToken cancellationToken)
    {
        var normalizedId = AdapterIdValidator.Normalize(adapterId);

        using var searcher = new ManagementObjectSearcher(
            "SELECT SettingID, DNSServerSearchOrder FROM Win32_NetworkAdapterConfiguration");

        foreach (ManagementObject result in searcher.Get())
        {
            cancellationToken.ThrowIfCancellationRequested();

            using (result)
            {
                var settingId = result["SettingID"] as string;
                if (settingId is null)
                    continue;

                if (!string.Equals(AdapterIdValidator.Normalize(settingId), normalizedId, StringComparison.OrdinalIgnoreCase))
                    continue;

                return (string[]?)result["DNSServerSearchOrder"] ?? Array.Empty<string>();
            }
        }

        return Array.Empty<string>();
    }
}
