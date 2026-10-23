using NetPulse.Domain.Diagnostics;
using NetPulse.Domain.Network;
using NetPulse.Domain.Optimization;

namespace NetPulse.Domain.Recommendations.Rules;

/// <summary>
/// Explicitly recommends doing nothing when the measured state is already healthy, per the
/// brief's requirement that a profile/engine be allowed to recommend no change at all
/// (§7, §H). This exists so "no recommendations fired" is never silently indistinguishable
/// from "nothing was checked."
/// </summary>
public sealed class HealthyNoActionRule : IRecommendationRule
{
    public OptimizationDescriptor Descriptor { get; } = new(
        Id: "recommend.healthy-no-action",
        Title: "No change recommended",
        Objective: "Avoid changing a setting that is not causing a measured problem.",
        Evidence: "Latency, jitter, and packet loss are all within the normal range measured for this target.",
        Preconditions: new[] { "The diagnostic finding's confidence is Healthy." },
        Risk: OptimizationRisk.Low,
        RequiredPermissions: Array.Empty<string>(),
        VerificationMethod: "Re-run periodically; re-evaluate only if a new diagnostic shows loss, jitter, or median latency outside the normal range.",
        SuccessCriterion: "Not applicable - there is nothing to verify because nothing is proposed.",
        FailureCriterion: "Not applicable.",
        RollbackMethod: "Not applicable - no change is proposed.",
        ReferenceDocumentation: new[] { "Project brief §7 and §H: a profile may recommend no change when the current state is already adequate." },
        KnownLimitations: new[] { "Reflects only the target and time window just measured, not a guarantee of future stability." });

    public TriggeredRecommendation? Evaluate(LatencyStatistics statistics, DiagnosticFinding finding)
    {
        if (finding.Confidence != DiagnosticConfidence.Healthy)
            return null;

        return new TriggeredRecommendation(
            statistics.Target,
            Descriptor,
            $"{statistics.Target} is within normal range: median {statistics.Median.TotalMilliseconds:F1} ms, {statistics.PacketLossPercent:F1}% loss, jitter {statistics.JitterMeanAbsoluteDeviation.TotalMilliseconds:F1} ms.",
            finding.Confidence);
    }
}
