using NetPulse.Domain.Network;

namespace NetPulse.Application.Ports;

/// <summary>
/// Generates real upload or download network traffic for a bounded, caller-specified
/// duration - the only port in this project whose entire purpose is to consume bandwidth.
/// Per the project's non-negotiable constraint, no automated session may trigger a real run
/// of this against the internet; only a human, interactively, from the running app. An
/// implementation must honor cancellation promptly (within roughly one request/response
/// cycle, not just at the end of the requested duration) and must never run longer than
/// <paramref name="duration"/> asks for.
/// </summary>
public interface ILoadGenerationService
{
    Task GenerateLoadAsync(LoadDirection direction, TimeSpan duration, CancellationToken cancellationToken);
}
