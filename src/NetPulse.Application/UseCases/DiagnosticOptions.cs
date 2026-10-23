namespace NetPulse.Application.UseCases;

/// <summary>
/// Caller-controlled bounds on a diagnostic run. There is no default that probes
/// continuously or without limit - every run is a bounded batch the user (or a scheduled
/// profile) explicitly asked for, per the "no unnecessary background polling" principle.
/// </summary>
public sealed record DiagnosticOptions(
    IReadOnlyList<string> Targets,
    int SampleCount = 20,
    TimeSpan? IntervalBetweenSamples = null,
    TimeSpan? ProbeTimeout = null,
    int TcpConfirmationPort = 443)
{
    // Nullable, not a TimeSpan defaulting to `default`: TimeSpan.Zero IS default(TimeSpan),
    // so a caller explicitly requesting a zero interval/timeout would have been silently
    // overridden back to the 200 ms / 2 s fallback - found by a performance test asserting
    // actual elapsed time instead of just correctness.
    public TimeSpan EffectiveInterval => IntervalBetweenSamples ?? TimeSpan.FromMilliseconds(200);
    public TimeSpan EffectiveTimeout => ProbeTimeout ?? TimeSpan.FromSeconds(2);

    // 443 (HTTPS) is the most broadly-open port for an arbitrary host, but it is still just
    // a guess for a target that doesn't run a web server on it - a failed TCP confirmation
    // means "this port didn't answer," not "this host is down." ConnectionDiagnosticUseCase's
    // classification text says so explicitly rather than treating it as a second proof of loss.
}
