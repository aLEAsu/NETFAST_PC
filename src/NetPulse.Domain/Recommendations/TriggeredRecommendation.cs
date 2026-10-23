using NetPulse.Domain.Diagnostics;
using NetPulse.Domain.Optimization;

namespace NetPulse.Domain.Recommendations;

/// <summary>
/// The output of a single rule firing against one target's diagnostic result. This is a
/// recommendation only - Phase 4 never applies, previews as "simulated", or stages any
/// change. <see cref="Descriptor"/> carries the full evidence/risk/rollback contract so the
/// user sees everything an eventual Phase 5 <see cref="IOptimizationAction"/> would need to
/// declare, even though no such action exists yet.
/// </summary>
public sealed record TriggeredRecommendation(
    string Target,
    OptimizationDescriptor Descriptor,
    string TriggerExplanation,
    DiagnosticConfidence BasisConfidence);
