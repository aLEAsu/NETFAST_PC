namespace NetPulse.Domain.Network;

/// <summary>
/// One probe attempt. <see cref="Responded"/> distinguishes "no ICMP reply" (which many
/// routers/firewalls suppress deliberately) from a confirmed loss of application traffic -
/// callers must not conflate the two when reporting results to the user.
/// </summary>
public sealed record LatencySample(
    string Target,
    DateTimeOffset Timestamp,
    bool Responded,
    TimeSpan? RoundTripTime);
