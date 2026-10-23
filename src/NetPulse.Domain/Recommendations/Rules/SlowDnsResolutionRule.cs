using NetPulse.Domain.Diagnostics;
using NetPulse.Domain.Optimization;

namespace NetPulse.Domain.Recommendations.Rules;

/// <summary>
/// Fires when DNS resolution for a hostname target failed or was slow. This is the one
/// recommendation in this build with a real, applyable action behind it:
/// <c>NetPulse.PrivilegedAgent.SetAdapterDnsServersAction</c>. That action lives in a
/// project Domain cannot reference (it would be circular - PrivilegedAgent depends on
/// Domain), so the link is by convention, not by object reference: the UI layer recognizes
/// this rule's <see cref="OptimizationDescriptor.Id"/> and offers to run that action when it
/// fires. This rule itself never constructs or runs anything - it only observes evidence.
/// </summary>
public sealed class SlowDnsResolutionRule : IRecommendationRule
{
    public static readonly TimeSpan ResolutionTimeThreshold = TimeSpan.FromMilliseconds(100);

    public OptimizationDescriptor Descriptor { get; } = new(
        Id: "recommend.slow-dns-resolution",
        Title: "Try a different DNS server",
        Objective: "Reduce DNS lookup failures or slow resolution by using a different resolver.",
        Evidence: "DNS resolution for this hostname either failed or took longer than expected.",
        Preconditions: new[] { "The target is a hostname, not an IP literal.", $"Resolution failed, or took longer than {ResolutionTimeThreshold.TotalMilliseconds:F0} ms." },
        Risk: OptimizationRisk.Low,
        RequiredPermissions: Array.Empty<string>(),
        VerificationMethod: "Re-run DNS resolution for the same hostname after switching and compare resolution time and success.",
        SuccessCriterion: "Resolution succeeds and completes faster than the original measurement.",
        FailureCriterion: "Resolution still fails or is not meaningfully faster after switching.",
        RollbackMethod: "Not applicable - this recommendation itself only suggests a comparison; if the user applies the underlying SetAdapterDnsServersAction, that action carries its own independent rollback.",
        ReferenceDocumentation: new[] { "Project brief Module D: DNS configuration changes must be based on a diagnosed problem, not applied speculatively." },
        KnownLimitations: new[]
        {
            "A single slow or failed lookup can be transient network noise - confirm across more than one run before concluding the configured DNS server itself is the cause.",
            "Switching DNS servers changes which resolver answers queries; it does not guarantee faster resolution for every domain, and some networks override DNS at the router/DHCP level regardless of this setting.",
        });

    public TriggeredRecommendation? Evaluate(NetworkEvidence evidence)
    {
        var dns = evidence.DnsCheck;

        if (!dns.IsApplicable)
            return null;

        bool slowOrFailed = !dns.Resolved || (dns.ResolutionTime is { } time && time > ResolutionTimeThreshold);
        if (!slowOrFailed)
            return null;

        var explanation = dns.Resolved
            ? $"DNS resolution for {dns.Hostname} took {dns.ResolutionTime!.Value.TotalMilliseconds:F0} ms, above the {ResolutionTimeThreshold.TotalMilliseconds:F0} ms threshold."
            : $"DNS resolution for {dns.Hostname} failed: {dns.Error}";

        return new TriggeredRecommendation(evidence.Statistics.Target, Descriptor, explanation, evidence.Finding.Confidence);
    }
}
