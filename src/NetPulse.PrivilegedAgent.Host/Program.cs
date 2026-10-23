using System.Management;
using System.Text.Json;
using NetPulse.Domain.PrivilegedOperations;

namespace NetPulse.PrivilegedAgent.Host;

/// <summary>
/// The ONLY executable in this solution that ever writes network configuration, and the
/// only one that runs elevated (see app.manifest). It trusts nothing it receives on the
/// command line - every argument is re-validated here with the exact same rules the caller
/// already applied, because the brief requires the privileged boundary to never assume a
/// caller validated correctly. There is no way to reach any operation other than the one
/// fixed, compiled capability below: this file is the entire allow-list.
///
/// Never invoked for real by the automated session that wrote it - see the remarks on
/// ElevatedHelperOperationLauncher for why.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        string? resultFilePath = args.Length >= 4 ? args[3] : null;

        try
        {
            var result = Run(args);
            WriteResult(resultFilePath, result);
            return result.Success ? 0 : 1;
        }
        catch (Exception ex)
        {
            // Never let an unhandled exception leave the caller without a result file -
            // the launcher only knows what happened through that file.
            WriteResult(resultFilePath, new HostResult(false, null, ex.Message));
            return 1;
        }
    }

    private static HostResult Run(string[] args)
    {
        if (args.Length != 4)
            return new HostResult(false, null, $"Expected 4 arguments (capability, adapterId, dnsServers, resultFilePath), got {args.Length}.");

        var (capabilityName, adapterId, dnsServersCsv, resultFilePath) = (args[0], args[1], args[2], args[3]);

        if (!Enum.TryParse<PrivilegedCapability>(capabilityName, out var capability))
            return new HostResult(false, null, $"'{capabilityName}' is not an allow-listed capability.");

        if (!IsResultPathAllowed(resultFilePath))
            return new HostResult(false, null, "Result file path is outside the allowed directory.");

        if (!AdapterIdValidator.TryValidate(adapterId, out var adapterError))
            return new HostResult(false, null, adapterError);

        var dnsServers = dnsServersCsv.Split(',', StringSplitOptions.RemoveEmptyEntries);
        if (!DnsServerAddressValidator.TryValidate(dnsServers, out var dnsError))
            return new HostResult(false, null, dnsError);

        return capability switch
        {
            PrivilegedCapability.SetAdapterDnsServers => SetAdapterDnsServers(adapterId, dnsServers),
            _ => new HostResult(false, null, $"Capability '{capability}' has no implementation."),
        };
    }

    private static bool IsResultPathAllowed(string resultFilePath)
    {
        var allowedDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NetPulseOptimizer",
            "elevated-results");

        var fullPath = Path.GetFullPath(resultFilePath);
        var fullAllowedDirectory = Path.GetFullPath(allowedDirectory) + Path.DirectorySeparatorChar;

        return fullPath.StartsWith(fullAllowedDirectory, StringComparison.OrdinalIgnoreCase)
            && Path.GetExtension(fullPath).Equals(".json", StringComparison.OrdinalIgnoreCase);
    }

    private static HostResult SetAdapterDnsServers(string adapterId, string[] dnsServers)
    {
        var normalizedTarget = AdapterIdValidator.Normalize(adapterId);

        using var searcher = new ManagementObjectSearcher(
            "SELECT SettingID FROM Win32_NetworkAdapterConfiguration");

        foreach (ManagementObject candidate in searcher.Get())
        {
            using (candidate)
            {
                var settingId = candidate["SettingID"] as string;
                if (settingId is null || AdapterIdValidator.Normalize(settingId) != normalizedTarget)
                    continue;

                using var inParams = candidate.GetMethodParameters("SetDNSServerSearchOrder");
                // Win32_NetworkAdapterConfiguration.SetDNSServerSearchOrder clears any static
                // DNS override (reverting to DHCP-assigned servers) when this parameter is
                // null - an empty array is not documented to behave the same way, so the
                // "restore automatic DNS" case (dnsServers.Length == 0, see
                // DnsServerAddressValidator) must pass null, not Array.Empty<string>().
                inParams["DNSServerSearchOrder"] = dnsServers.Length == 0 ? null : dnsServers;

                using var outParams = candidate.InvokeMethod("SetDNSServerSearchOrder", inParams, null);
                var returnValue = Convert.ToUInt32(outParams?["ReturnValue"] ?? 1u);

                var successMessage = dnsServers.Length == 0
                    ? "Static DNS override cleared; adapter will use DHCP-assigned DNS servers."
                    : $"DNS servers set to {string.Join(",", dnsServers)}.";

                return returnValue == 0
                    ? new HostResult(true, successMessage, null)
                    : new HostResult(false, null, $"SetDNSServerSearchOrder returned error code {returnValue}.");
            }
        }

        return new HostResult(false, null, $"No adapter configuration matching '{adapterId}' was found.");
    }

    private static void WriteResult(string? resultFilePath, HostResult result)
    {
        if (string.IsNullOrWhiteSpace(resultFilePath) || !IsResultPathAllowed(resultFilePath))
            return;

        var directory = Path.GetDirectoryName(resultFilePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(resultFilePath, JsonSerializer.Serialize(result));
    }

    private sealed record HostResult(bool Success, string? Output, string? Error);
}
