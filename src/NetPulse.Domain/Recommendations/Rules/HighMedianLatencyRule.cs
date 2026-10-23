using NetPulse.Domain.Diagnostics;
using NetPulse.Domain.Optimization;

namespace NetPulse.Domain.Recommendations.Rules;

/// <summary>
/// Flags consistently high latency even when loss and jitter are both low - a signal the
/// loss/jitter-only classification in ConnectionDiagnosticUseCase.Classify does not surface
/// on its own. Deliberately does not blame Windows: distance to the target, Wi-Fi vs.
/// Ethernet, and the ISP's own path are all more common causes than any local setting.
/// </summary>
public sealed class HighMedianLatencyRule : IRecommendationRule
{
    private static readonly TimeSpan MedianThreshold = TimeSpan.FromMilliseconds(100);

    public OptimizationDescriptor Descriptor { get; } = new(
        Id: "recommend.high-median-latency",
        Title: "Check Wi-Fi vs. Ethernet and target distance before assuming a fixable problem",
        Objective: "Avoid treating normal geographic/path latency as a defect fixable by a local change.",
        Evidence: "Median round-trip time to this target is consistently high even though loss and jitter are both within normal range.",
        Preconditions: new[] { $"Median RTT > {MedianThreshold.TotalMilliseconds:F0} ms.", "Packet loss and jitter are both within normal range for this run." },
        Risk: OptimizationRisk.Low,
        RequiredPermissions: Array.Empty<string>(),
        VerificationMethod: "Compare against a wired connection (if currently on Wi-Fi) and against a geographically closer target.",
        SuccessCriterion: "A wired connection or closer target shows meaningfully lower median latency, pointing at the original setup rather than this PC.",
        FailureCriterion: "Latency stays high even wired and against a close target, suggesting an ISP-side or routing issue outside this PC's control.",
        RollbackMethod: "Not applicable - this recommendation only suggests a comparison measurement.",
        ReferenceDocumentation: new[] { "Project brief §2: distinguish what is under the PC's control from what depends on the router/ISP/remote infrastructure." },
        KnownLimitations: new[] { "There is no universal 'good' latency number; the right baseline depends on physical distance to the target." });

    public TriggeredRecommendation? Evaluate(NetworkEvidence evidence)
    {
        var (statistics, finding) = (evidence.Statistics, evidence.Finding);

        if (finding.Confidence == DiagnosticConfidence.Hypothesis)
            return null;

        if (statistics.Median <= MedianThreshold)
            return null;

        return new TriggeredRecommendation(
            statistics.Target,
            Descriptor,
            $"Median latency to {statistics.Target} is {statistics.Median.TotalMilliseconds:F1} ms with {statistics.PacketLossPercent:F1}% loss.",
            finding.Confidence);
    }
}
