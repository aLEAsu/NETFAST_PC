using NetPulse.Domain.Network;

namespace NetPulse.Application.UseCases;

/// <summary>
/// Caller-controlled bounds on a bufferbloat run - mirrors <see cref="DiagnosticOptions"/>'s
/// "no default that runs unbounded" principle, with an explicit hard cap on load duration
/// enforced by <see cref="BufferbloatUseCase"/> regardless of what a caller passes, since
/// this run is the one place in the app that deliberately consumes bandwidth.
/// </summary>
public sealed record BufferbloatOptions(
    string Target,
    LoadDirection Direction,
    TimeSpan LoadDuration,
    int RestSampleCount = 10,
    TimeSpan? SampleInterval = null,
    TimeSpan? ProbeTimeout = null)
{
    public TimeSpan EffectiveSampleInterval => SampleInterval ?? TimeSpan.FromMilliseconds(200);
    public TimeSpan EffectiveProbeTimeout => ProbeTimeout ?? TimeSpan.FromSeconds(2);
}
