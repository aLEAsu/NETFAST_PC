using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using NetPulse.Application.Ports;
using NetPulse.Domain.Network;

namespace NetPulse.Infrastructure.Windows.Network;

/// <summary>
/// Resolves a hostname once and times it. If the target is already an IP literal, resolution
/// does not apply - callers should check that themselves (via <see cref="IPAddress.TryParse"/>)
/// before calling this, same as <see cref="DnsResolutionSample.NotApplicable"/> documents.
/// </summary>
public sealed class DnsResolutionProbeService : IDnsResolutionProbeService
{
    public async Task<DnsResolutionSample> ProbeOnceAsync(string hostname, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var timestamp = DateTimeOffset.UtcNow;
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var entry = await Dns.GetHostEntryAsync(hostname, timeoutCts.Token);
            stopwatch.Stop();

            var addresses = entry.AddressList.Select(a => a.ToString()).ToArray();
            return new DnsResolutionSample(hostname, timestamp, Resolved: true, stopwatch.Elapsed, addresses, Error: null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new DnsResolutionSample(hostname, timestamp, Resolved: false, ResolutionTime: null, Array.Empty<string>(), Error: "DNS resolution timed out.");
        }
        catch (SocketException ex)
        {
            return new DnsResolutionSample(hostname, timestamp, Resolved: false, ResolutionTime: null, Array.Empty<string>(), Error: ex.Message);
        }
    }
}
