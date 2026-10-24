using System.Management;
using NetPulse.Application.Ports;
using NetPulse.Domain.Network;

namespace NetPulse.Infrastructure.Windows.Network;

/// <summary>
/// Read-only WMI query against Win32_PerfFormattedData_Tcpip_NetworkInterface - Windows'
/// own already-computed per-second rate, not a value this project derives from two raw
/// samples itself. "Formatted" perf classes (as opposed to "Raw") return the rate directly,
/// which is why a single query is enough here, unlike a raw-counter class that would need
/// two samples and a manual delta.
/// </summary>
public sealed class WmiAdapterThroughputReader : IAdapterThroughputReader
{
    public Task<IReadOnlyList<AdapterThroughputSample>> GetCurrentThroughputAsync(CancellationToken cancellationToken) =>
        Task.Run(() => Query(cancellationToken), cancellationToken);

    private static IReadOnlyList<AdapterThroughputSample> Query(CancellationToken cancellationToken)
    {
        var sampledAt = DateTimeOffset.UtcNow;
        var results = new List<AdapterThroughputSample>();

        using var searcher = new ManagementObjectSearcher(
            "SELECT Name, BytesSentPersec, BytesReceivedPersec FROM Win32_PerfFormattedData_Tcpip_NetworkInterface");

        foreach (ManagementObject result in searcher.Get())
        {
            cancellationToken.ThrowIfCancellationRequested();

            using (result)
            {
                var name = result["Name"] as string;
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                var sent = Convert.ToDouble(result["BytesSentPersec"] ?? 0d);
                var received = Convert.ToDouble(result["BytesReceivedPersec"] ?? 0d);

                results.Add(new AdapterThroughputSample(name, sent, received, sampledAt));
            }
        }

        return results;
    }
}
