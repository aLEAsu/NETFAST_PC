using NetPulse.Domain.Network;

namespace NetPulse.Application.Ports;

/// <summary>
/// Port for adapter enumeration. Implemented by NetPulse.Infrastructure.Windows; a fake
/// implementation can be substituted in tests so suites never touch the real machine's
/// network configuration.
/// </summary>
public interface INetworkAdapterInventoryService
{
    Task<IReadOnlyList<NetworkAdapterInfo>> GetAdaptersAsync(CancellationToken cancellationToken);
}
