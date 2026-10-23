using NetPulse.Domain.Diagnostics;
using NetPulse.Domain.Optimization;

namespace NetPulse.Domain.Recommendations.Rules;

/// <summary>
/// Significant packet loss is reported, but the rule deliberately does not propose a Windows
/// setting change: the brief requires that loss not be attributed to this PC before the
/// router, cable, driver, and ISP are ruled out (§2.6, §12).
/// </summary>
public sealed class PacketLossInvestigateRule : IRecommendationRule
{
    private const double LossThresholdPercent = 2.0;
    private const int MinimumSamplesForConfidence = 10;

    public OptimizationDescriptor Descriptor { get; } = new(
        Id: "recommend.packet-loss-investigate",
        Title: "Investigate packet loss outside Windows before changing any setting",
        Objective: "Rule out router, cable, driver, and ISP causes before attributing loss to this PC.",
        Evidence: "A meaningful share of ICMP probes went unanswered across enough samples to not be noise.",
        Preconditions: new[] { $"Packet loss >= {LossThresholdPercent}%.", $"At least {MinimumSamplesForConfidence} samples were collected." },
        Risk: OptimizationRisk.Low,
        RequiredPermissions: Array.Empty<string>(),
        VerificationMethod: "Repeat the diagnostic while bypassing Wi-Fi (wired, if possible), directly against the router's own address, and after a router reboot.",
        SuccessCriterion: "Loss drops to the expected range once the router/cable/driver variables above are isolated one at a time.",
        FailureCriterion: "Loss persists even on a direct wired connection to the router with no other variable changed.",
        RollbackMethod: "Not applicable - this recommendation only suggests further diagnosis, not a system change.",
        ReferenceDocumentation: new[] { "Project brief §2.6 and §12: do not attribute loss to the PC without ruling out router/ISP/remote-target causes." },
        KnownLimitations: new[] { "ICMP loss does not confirm loss of this machine's own application traffic.", "The remote target or an intermediate hop may be the actual cause." });

    public TriggeredRecommendation? Evaluate(NetworkEvidence evidence)
    {
        var (statistics, finding) = (evidence.Statistics, evidence.Finding);

        if (finding.Confidence == DiagnosticConfidence.Hypothesis)
            return null;

        if (statistics.PacketLossPercent < LossThresholdPercent)
            return null;

        return new TriggeredRecommendation(
            statistics.Target,
            Descriptor,
            $"{statistics.PacketLossPercent:F1}% packet loss observed against {statistics.Target} across {statistics.SampleCount} samples.",
            finding.Confidence);
    }
}
