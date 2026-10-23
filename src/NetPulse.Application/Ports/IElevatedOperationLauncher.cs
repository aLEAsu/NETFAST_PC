using NetPulse.Domain.PrivilegedOperations;

namespace NetPulse.Application.Ports;

/// <summary>
/// Hands a validated request to a separate, per-operation elevated process and returns its
/// result. There is no persistent privileged service: the implementation launches
/// NetPulse.PrivilegedAgent.Host for exactly one capability, waits for it to exit, and the
/// elevated process is gone. The implementation must still treat the request as untrusted
/// and build the child process's command line from individually-validated, typed values -
/// never by concatenating a free-form string.
/// </summary>
public interface IElevatedOperationLauncher
{
    Task<PrivilegedOperationResult> ExecuteAsync(PrivilegedOperationRequest request, CancellationToken cancellationToken);
}
