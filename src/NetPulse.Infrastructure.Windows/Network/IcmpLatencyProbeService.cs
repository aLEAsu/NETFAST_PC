using System.Net.NetworkInformation;
using NetPulse.Application.Ports;
using NetPulse.Domain.Network;

namespace NetPulse.Infrastructure.Windows.Network;

/// <summary>
/// Single-probe ICMP echo. Deliberately does not loop internally - the use case layer
/// controls sample count and inter-sample delay so the application decides how much
/// traffic a diagnostic run generates, not this service.
/// </summary>
public sealed class IcmpLatencyProbeService : ILatencyProbeService
{
    public async Task<LatencySample> ProbeOnceAsync(string target, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var timestamp = DateTimeOffset.UtcNow;

        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(target, (int)timeout.TotalMilliseconds).WaitAsync(cancellationToken);

            if (reply.Status == IPStatus.Success)
                return new LatencySample(target, timestamp, Responded: true, RoundTripTime: TimeSpan.FromMilliseconds(reply.RoundtripTime));

            return new LatencySample(target, timestamp, Responded: false, RoundTripTime: null);
        }
        catch (PingException)
        {
            return new LatencySample(target, timestamp, Responded: false, RoundTripTime: null);
        }
    }
}
