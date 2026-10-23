using NetPulse.Domain.Network;

namespace NetPulse.Application.Ports;

public interface ILatencyProbeService
{
    /// <summary>Sends one probe to <paramref name="target"/> and returns the outcome.</summary>
    Task<LatencySample> ProbeOnceAsync(string target, TimeSpan timeout, CancellationToken cancellationToken);
}
