using System.Diagnostics;
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
            return new HostResult(false, null, $"Expected 4 arguments (capability, adapterId, parameters, resultFilePath), got {args.Length}.");

        var (capabilityName, adapterId, parametersCsv, resultFilePath) = (args[0], args[1], args[2], args[3]);

        if (!Enum.TryParse<PrivilegedCapability>(capabilityName, out var capability))
            return new HostResult(false, null, $"'{capabilityName}' is not an allow-listed capability.");

        if (!IsResultPathAllowed(resultFilePath))
            return new HostResult(false, null, "Result file path is outside the allowed directory.");

        // Each capability validates its own parameters here - they do not share a shape
        // (DNS needs an adapter id + a server list; a pktmon capture needs neither, just a
        // bounded duration), so there is deliberately no single validation step shared by
        // every capability before this dispatch.
        return capability switch
        {
            PrivilegedCapability.SetAdapterDnsServers => RunSetAdapterDnsServers(adapterId, parametersCsv),
            PrivilegedCapability.CapturePacketCounters => RunCapturePacketCounters(parametersCsv),
            _ => new HostResult(false, null, $"Capability '{capability}' has no implementation."),
        };
    }

    private static HostResult RunSetAdapterDnsServers(string adapterId, string dnsServersCsv)
    {
        if (!AdapterIdValidator.TryValidate(adapterId, out var adapterError))
            return new HostResult(false, null, adapterError);

        var dnsServers = dnsServersCsv.Split(',', StringSplitOptions.RemoveEmptyEntries);
        if (!DnsServerAddressValidator.TryValidate(dnsServers, out var dnsError))
            return new HostResult(false, null, dnsError);

        return SetAdapterDnsServers(adapterId, dnsServers);
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

    /// <summary>
    /// Runs a bounded pktmon counters-only session and returns its raw, unparsed text
    /// output - never packet content, never a parsed/derived number this project did not
    /// directly observe. "--comp nics" scopes the count to network adapters only (pktmon's
    /// own documented selector keyword, not a specific adapter id - enumerating a specific
    /// adapter's component id itself requires an elevated `pktmon list` call, which this
    /// first pass skips to keep the elevated code path simple and auditable). The hard
    /// duration cap is enforced here, independently of whatever the caller already checked,
    /// per the brief's requirement that the cap live at the privileged boundary itself.
    /// </summary>
    private static HostResult RunCapturePacketCounters(string durationText)
    {
        if (!PacketCaptureDurationValidator.TryValidate(durationText, out var durationSeconds, out var durationError))
            return new HostResult(false, null, durationError);

        try
        {
            RunPktmon("start", "--counters-only", "--comp", "nics");
            RunPktmon("reset");
            Thread.Sleep(TimeSpan.FromSeconds(durationSeconds));
            var countersOutput = RunPktmon("counters");
            RunPktmon("stop");
            RunPktmon("unload");

            return new HostResult(true, countersOutput, null);
        }
        catch (Exception ex)
        {
            // Best-effort cleanup even on failure, so a failed capture never leaves
            // pktmon's driver loaded and counting in the background.
            TryRunPktmonIgnoringErrors("stop");
            TryRunPktmonIgnoringErrors("unload");
            return new HostResult(false, null, $"pktmon capture failed: {ex.Message}");
        }
    }

    private static string RunPktmon(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("pktmon.exe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            // Without this, redirected output is read using the console's OEM codepage
            // (CP437 on this machine), not the UTF-8 pktmon actually writes - verified by a
            // real capture whose accented characters ("Dirección", "Recepción") came back
            // as mojibake ("Direcci├│n") until this was added.
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start pktmon.exe.");
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"pktmon {string.Join(' ', arguments)} exited with code {process.ExitCode}: {stderr}");

        return stdout;
    }

    private static void TryRunPktmonIgnoringErrors(params string[] arguments)
    {
        try
        {
            RunPktmon(arguments);
        }
        catch
        {
            // Cleanup is best-effort: if pktmon was never started, "stop"/"unload" failing
            // is expected, not a new error worth surfacing over the original failure.
        }
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
