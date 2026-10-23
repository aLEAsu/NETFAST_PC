using NetPulse.Domain.Diagnostics;
using NetPulse.Domain.Optimization;

namespace NetPulse.Domain.Recommendations.Rules;

/// <summary>
/// High jitter at rest is a hint, not proof, of bufferbloat/congestion - the brief requires
/// a dedicated latency-under-load comparison (§3.5 of the brief's §E) before concluding
/// that. This rule only ever recommends running that follow-up test, which does not exist
/// in this codebase yet (see documentation/02-roadmap-and-limitations.md).
/// </summary>
public sealed class HighJitterBufferbloatFollowUpRule : IRecommendationRule
{
    private static readonly TimeSpan JitterThreshold = TimeSpan.FromMilliseconds(30);

    public OptimizationDescriptor Descriptor { get; } = new(
        Id: "recommend.high-jitter-bufferbloat-followup",
        Title: "Run a bufferbloat (latency-under-load) test before concluding anything",
        Objective: "Confirm or rule out bufferbloat/congestion as the cause of the observed latency variability.",
        Evidence: "Latency between consecutive samples varies by more than the threshold at rest, which is consistent with (but not proof of) bufferbloat.",
        Preconditions: new[] { $"Jitter (mean absolute deviation between consecutive RTTs) > {JitterThreshold.TotalMilliseconds:F0} ms." },
        Risk: OptimizationRisk.Low,
        RequiredPermissions: Array.Empty<string>(),
        VerificationMethod: "Compare latency at rest vs. latency during a controlled, consented download/upload load (brief Module E) - not implemented yet.",
        SuccessCriterion: "Latency under load is not substantially higher than at rest, ruling out local bufferbloat.",
        FailureCriterion: "Latency under load rises sharply, consistent with bufferbloat likely fixable via QoS/SQM on the router.",
        RollbackMethod: "Not applicable - this recommendation only suggests a follow-up diagnostic.",
        ReferenceDocumentation: new[] { "Project brief Module E (bufferbloat diagnosis): requires a dedicated load test, not jitter-at-rest alone." },
        KnownLimitations: new[] { "The bufferbloat load test itself is not implemented in this codebase yet.", "High jitter can also come from Wi-Fi interference or a busy remote target, not only local queuing." });

    public TriggeredRecommendation? Evaluate(NetworkEvidence evidence)
    {
        var (statistics, finding) = (evidence.Statistics, evidence.Finding);

        if (finding.Confidence == DiagnosticConfidence.Hypothesis)
            return null;

        if (statistics.JitterMeanAbsoluteDeviation <= JitterThreshold)
            return null;

        return new TriggeredRecommendation(
            statistics.Target,
            Descriptor,
            $"Jitter of {statistics.JitterMeanAbsoluteDeviation.TotalMilliseconds:F1} ms observed against {statistics.Target} at rest.",
            finding.Confidence);
    }
}
