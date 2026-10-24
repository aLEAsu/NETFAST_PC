using NetPulse.Domain.Network;

namespace NetPulse.Application.Ports;

/// <summary>Read-only. Real Windows performance-counter rates, not a derived estimate.</summary>
public interface IAdapterThroughputReader
{
    Task<IReadOnlyList<AdapterThroughputSample>> GetCurrentThroughputAsync(CancellationToken cancellationToken);
}
