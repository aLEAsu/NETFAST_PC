using NetPulse.Domain.Network;

namespace NetPulse.Application.Ports;

public interface IDnsResolutionProbeService
{
    Task<DnsResolutionSample> ProbeOnceAsync(string hostname, TimeSpan timeout, CancellationToken cancellationToken);
}
