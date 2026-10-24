using NetPulse.Domain.Network;

namespace NetPulse.Application.Ports;

/// <summary>
/// Read-only, unelevated. Deliberately does not report bytes-per-process: Windows has no
/// unelevated, non-ETW API for that (Resource Monitor's own "Network" tab bytes figures rely
/// on an ETW session). Module F's brief requirement (§6 Module F) to "indicate clearly which
/// metrics are direct and which are estimates" is honored here by not shipping an estimate at
/// all for the one number this project cannot measure honestly - only real, directly observed
/// connection ownership (which process, which remote endpoint) is reported.
/// </summary>
public interface IProcessNetworkConnectionsReader
{
    Task<IReadOnlyList<ProcessNetworkConnection>> GetActiveConnectionsAsync(CancellationToken cancellationToken);
}
