using NetPulse.Domain.Network;

namespace NetPulse.Application.Ports;

public interface IDiagnosticHistoryRepository
{
    Task SaveAsync(LatencyStatistics statistics, CancellationToken cancellationToken);

    Task<IReadOnlyList<LatencyStatistics>> GetRecentAsync(string target, int count, CancellationToken cancellationToken);
}
