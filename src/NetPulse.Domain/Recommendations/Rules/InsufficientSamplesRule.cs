using NetPulse.Domain.Diagnostics;
using NetPulse.Domain.Optimization;

namespace NetPulse.Domain.Recommendations.Rules;

/// <summary>Never recommends a change - only recommends collecting more evidence first.</summary>
public sealed class InsufficientSamplesRule : IRecommendationRule
{
    public OptimizationDescriptor Descriptor { get; } = new(
        Id: "recommend.insufficient-samples",
        Title: "Collect more samples before drawing conclusions",
        Objective: "Avoid treating measurement noise as a real network problem.",
        Evidence: "Fewer samples were collected than the minimum needed to distinguish a real effect from random variation.",
        Preconditions: new[] { "The diagnostic finding's confidence is Hypothesis due to low sample count." },
        Risk: OptimizationRisk.Low,
        RequiredPermissions: Array.Empty<string>(),
        VerificationMethod: "Re-run the diagnostic with SampleCount >= 10 against the same target.",
        SuccessCriterion: "The re-run produces a Confirmed, Probable, ExternalLimitation, or Healthy finding instead of Hypothesis.",
        FailureCriterion: "The re-run still has fewer than 10 samples (e.g. it was cancelled early).",
        RollbackMethod: "Not applicable - this recommendation only suggests re-running a read-only diagnostic.",
        ReferenceDocumentation: new[] { "Project brief §7: the engine must not attribute effects without sufficient samples." },
        KnownLimitations: new[] { "Does not itself fix anything; it only improves the confidence of the next diagnostic." });

    public TriggeredRecommendation? Evaluate(NetworkEvidence evidence)
    {
        var (statistics, finding) = (evidence.Statistics, evidence.Finding);

        if (finding.Confidence != DiagnosticConfidence.Hypothesis)
            return null;

        return new TriggeredRecommendation(
            statistics.Target,
            Descriptor,
            $"Only {statistics.SampleCount} sample(s) were collected for {statistics.Target}; re-run with more samples before acting on this result.",
            finding.Confidence);
    }
}
