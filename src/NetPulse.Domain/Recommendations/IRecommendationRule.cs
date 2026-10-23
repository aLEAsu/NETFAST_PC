using NetPulse.Domain.Diagnostics;
using NetPulse.Domain.Network;
using NetPulse.Domain.Optimization;

namespace NetPulse.Domain.Recommendations;

/// <summary>
/// A single deterministic, evidence-based rule. Implementations must be pure - no I/O, no
/// randomness, no clock reads beyond what is already in <see cref="LatencyStatistics"/> -
/// so the same inputs always produce the same verdict and the rule can be unit-tested
/// without touching the network or the registry.
/// </summary>
public interface IRecommendationRule
{
    /// <summary>Static metadata describing what this rule is about, independent of any one run.</summary>
    OptimizationDescriptor Descriptor { get; }

    /// <summary>
    /// Returns a <see cref="TriggeredRecommendation"/> if this run's evidence meets the rule's
    /// bar, or null if it does not apply. Must never throw for a well-formed, non-empty
    /// <paramref name="statistics"/>/<paramref name="finding"/> pair.
    /// </summary>
    TriggeredRecommendation? Evaluate(LatencyStatistics statistics, DiagnosticFinding finding);
}
