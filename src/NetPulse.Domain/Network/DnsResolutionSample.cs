namespace NetPulse.Domain.Network;

/// <summary>
/// One DNS lookup attempt for a hostname target. Not meaningful for a target that is
/// already an IP literal - callers should not probe DNS resolution for those and should
/// represent that case as <see cref="NotApplicable"/> rather than a failed lookup.
/// </summary>
public sealed record DnsResolutionSample(
    string Hostname,
    DateTimeOffset Timestamp,
    bool Resolved,
    TimeSpan? ResolutionTime,
    string[] ResolvedAddresses,
    string? Error,
    bool IsApplicable = true)
{
    public static DnsResolutionSample NotApplicable(string target) =>
        new(target, DateTimeOffset.UtcNow, Resolved: false, ResolutionTime: null, ResolvedAddresses: Array.Empty<string>(), Error: null, IsApplicable: false);
}
