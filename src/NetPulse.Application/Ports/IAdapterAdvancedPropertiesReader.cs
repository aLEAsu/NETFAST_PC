using NetPulse.Domain.Network;

namespace NetPulse.Application.Ports;

/// <summary>
/// Read-only. Module C (brief §6.4) is explicit that this ships with no write path at all
/// in this pass - no universal "best" values exist for RSS/offloads/EEE, and the brief
/// wants a concrete, evidenced optimization rule before any write capability is even
/// considered, through the same elevation boundary <see cref="IElevatedOperationLauncher"/>
/// already establishes. This interface has no method that could change a property, so a bug
/// or misuse here cannot alter adapter configuration.
/// </summary>
public interface IAdapterAdvancedPropertiesReader
{
    Task<IReadOnlyList<NetworkAdapterAdvancedProperty>> GetAdvancedPropertiesAsync(string adapterId, CancellationToken cancellationToken);
}
