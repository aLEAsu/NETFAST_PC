namespace NetPulse.Application.Ports;

/// <summary>
/// Read-only. Implementations must never write anything - the capability to change DNS
/// configuration lives only in the elevated helper reached through
/// <see cref="IElevatedOperationLauncher"/>, never in the unelevated app's own dependency
/// graph, so a bug or compromise in this process cannot by itself change network settings.
/// </summary>
public interface IAdapterDnsConfigurationReader
{
    Task<IReadOnlyList<string>> GetDnsServersAsync(string adapterId, CancellationToken cancellationToken);
}
