namespace NetPulse.Application.UseCases;

/// <summary>
/// Caller-controlled bounds on a diagnostic run. There is no default that probes
/// continuously or without limit - every run is a bounded batch the user (or a scheduled
/// profile) explicitly asked for, per the "no unnecessary background polling" principle.
/// </summary>
public sealed record DiagnosticOptions(
    IReadOnlyList<string> Targets,
    int SampleCount = 20,
    TimeSpan IntervalBetweenSamples = default,
    TimeSpan ProbeTimeout = default)
{
    public TimeSpan EffectiveInterval => IntervalBetweenSamples == default ? TimeSpan.FromMilliseconds(200) : IntervalBetweenSamples;
    public TimeSpan EffectiveTimeout => ProbeTimeout == default ? TimeSpan.FromSeconds(2) : ProbeTimeout;
}
