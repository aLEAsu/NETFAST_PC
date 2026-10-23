using System.Management;
using NetPulse.Application.Ports;
using NetPulse.Domain.Network;
using NetPulse.Domain.PrivilegedOperations;

namespace NetPulse.Infrastructure.Windows.Network;

/// <summary>
/// Read-only WMI query against MSFT_NetAdapterAdvancedPropertySettingData in the
/// root\StandardCimv2 namespace - the same CIM class PowerShell's
/// Get-NetAdapterAdvancedProperty reads from. Unlike Win32_NetworkAdapterConfiguration
/// (used by <see cref="WmiAdapterDnsConfigurationReader"/>), this class lives outside the
/// default root\cimv2 namespace, so the query needs an explicit ManagementScope. This class
/// has no write path at all - see <see cref="IAdapterAdvancedPropertiesReader"/>'s remarks.
/// </summary>
public sealed class WmiAdapterAdvancedPropertiesReader : IAdapterAdvancedPropertiesReader
{
    private const string Namespace = @"root\StandardCimv2";

    public Task<IReadOnlyList<NetworkAdapterAdvancedProperty>> GetAdvancedPropertiesAsync(string adapterId, CancellationToken cancellationToken)
    {
        if (!AdapterIdValidator.TryValidate(adapterId, out var error))
            throw new ArgumentException(error, nameof(adapterId));

        // System.Management has no async API - run on the thread pool so a WPF caller's UI
        // thread stays free, same pattern as WmiAdapterDnsConfigurationReader.
        return Task.Run(() => QueryAdvancedProperties(adapterId, cancellationToken), cancellationToken);
    }

    private static IReadOnlyList<NetworkAdapterAdvancedProperty> QueryAdvancedProperties(string adapterId, CancellationToken cancellationToken)
    {
        var normalizedId = AdapterIdValidator.Normalize(adapterId);
        var results = new List<NetworkAdapterAdvancedProperty>();

        var scope = new ManagementScope(Namespace);
        using var searcher = new ManagementObjectSearcher(
            scope,
            new ObjectQuery(
                "SELECT InstanceID, RegistryKeyword, DisplayName, DisplayValue, RegistryValue " +
                "FROM MSFT_NetAdapterAdvancedPropertySettingData"));

        foreach (ManagementBaseObject result in searcher.Get())
        {
            cancellationToken.ThrowIfCancellationRequested();

            using (result)
            {
                // This class has no InterfaceGuid property - the adapter's GUID is the
                // prefix of InstanceID, e.g. "{E3C831C4-...}::*EEE" (verified against this
                // machine's real Ethernet adapter via Get-CimInstance).
                var instanceId = result["InstanceID"] as string;
                var adapterGuid = ExtractAdapterGuid(instanceId);
                if (adapterGuid is null ||
                    !string.Equals(AdapterIdValidator.Normalize(adapterGuid), normalizedId, StringComparison.OrdinalIgnoreCase))
                    continue;

                var keyword = result["RegistryKeyword"] as string;
                if (string.IsNullOrWhiteSpace(keyword))
                    continue; // Some rows expose no stable keyword - nothing a user could act on.

                var displayName = result["DisplayName"] as string ?? keyword;
                var displayValue = FirstOrDash(result["DisplayValue"] as string[]);
                var rawValue = FirstOrDash(result["RegistryValue"] as string[]);

                results.Add(new NetworkAdapterAdvancedProperty(keyword, displayName, displayValue, rawValue));
            }
        }

        return results;
    }

    private static string FirstOrDash(string[]? values) =>
        values is { Length: > 0 } ? string.Join(", ", values) : "-";

    private static string? ExtractAdapterGuid(string? instanceId)
    {
        if (string.IsNullOrEmpty(instanceId))
            return null;

        var separatorIndex = instanceId.IndexOf("::", StringComparison.Ordinal);
        return separatorIndex > 0 ? instanceId[..separatorIndex] : null;
    }
}
