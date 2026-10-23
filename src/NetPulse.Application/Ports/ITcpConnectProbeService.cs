using NetPulse.Domain.Network;

namespace NetPulse.Application.Ports;

public interface ITcpConnectProbeService
{
    Task<TcpConnectSample> ProbeOnceAsync(string target, int port, TimeSpan timeout, CancellationToken cancellationToken);
}
