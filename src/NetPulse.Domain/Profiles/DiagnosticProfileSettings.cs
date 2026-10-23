using NetPulse.Domain.Recommendations;

namespace NetPulse.Domain.Profiles;

/// <summary>
/// What one profile actually changes: diagnostic parameters (how it measures) and which
/// recommendation ids to surface first (how results are presented) - never which action
/// gets applied. Every recommendation this build can produce is still exactly what
/// DefaultRecommendationRules would have produced without a profile selected; a profile
/// only reorders the same, unmodified list.
/// </summary>
public sealed record DiagnosticProfileSettings(
    DiagnosticProfile Profile,
    string DisplayName,
    string Focus,
    int SampleCount,
    TimeSpan IntervalBetweenSamples,
    TimeSpan ProbeTimeout,
    IReadOnlyList<string> PriorityRecommendationIds)
{
    /// <summary>
    /// Stable reorder, not a filter - nothing is hidden or dropped, recommendations this
    /// profile doesn't prioritize just sort after the ones it does, in their original order.
    /// </summary>
    public IReadOnlyList<TriggeredRecommendation> Prioritize(IReadOnlyList<TriggeredRecommendation> recommendations) =>
        recommendations
            .OrderByDescending(r => PriorityRecommendationIds.Contains(r.Descriptor.Id))
            .ToList();
}
