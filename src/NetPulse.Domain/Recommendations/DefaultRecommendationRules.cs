using NetPulse.Domain.Recommendations.Rules;

namespace NetPulse.Domain.Recommendations;

/// <summary>
/// The fixed, reviewed set of rules this build ships with. There is no plugin mechanism and
/// no way to load a rule from outside the compiled assembly - every rule is a reviewed class
/// in this namespace, per the brief's requirement that the engine be deterministic and
/// auditable rather than configurable by arbitrary external input.
/// </summary>
public static class DefaultRecommendationRules
{
    public static IReadOnlyList<IRecommendationRule> Create() => new IRecommendationRule[]
    {
        new InsufficientSamplesRule(),
        new PacketLossInvestigateRule(),
        new HighJitterBufferbloatFollowUpRule(),
        new HighMedianLatencyRule(),
        new HealthyNoActionRule(),
    };
}
