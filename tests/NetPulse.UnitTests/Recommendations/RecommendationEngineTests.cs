using NetPulse.Domain.Diagnostics;
using NetPulse.Domain.Network;
using NetPulse.Domain.Optimization;
using NetPulse.Domain.Recommendations;
using static NetPulse.UnitTests.Recommendations.RecommendationRuleTestHelpers;

namespace NetPulse.UnitTests.Recommendations;

public class RecommendationEngineTests
{
    private sealed class AlwaysFiresRule : IRecommendationRule
    {
        public OptimizationDescriptor Descriptor { get; } = new(
            "rule.always", "Always fires", "test", "test", Array.Empty<string>(),
            OptimizationRisk.Low, Array.Empty<string>(), "n/a", "n/a", "n/a", "n/a",
            Array.Empty<string>(), Array.Empty<string>());

        public TriggeredRecommendation? Evaluate(LatencyStatistics statistics, DiagnosticFinding finding) =>
            new(statistics.Target, Descriptor, "always fires", finding.Confidence);
    }

    private sealed class NeverFiresRule : IRecommendationRule
    {
        public OptimizationDescriptor Descriptor { get; } = new(
            "rule.never", "Never fires", "test", "test", Array.Empty<string>(),
            OptimizationRisk.Low, Array.Empty<string>(), "n/a", "n/a", "n/a", "n/a",
            Array.Empty<string>(), Array.Empty<string>());

        public TriggeredRecommendation? Evaluate(LatencyStatistics statistics, DiagnosticFinding finding) => null;
    }

    [Fact]
    public void Evaluate_OnlyCollectsRulesThatFire()
    {
        var engine = new RecommendationEngine(new IRecommendationRule[] { new AlwaysFiresRule(), new NeverFiresRule() });

        var results = engine.Evaluate(Stats(), Finding(DiagnosticConfidence.Confirmed));

        Assert.Single(results);
        Assert.Equal("rule.always", results[0].Descriptor.Id);
    }

    [Fact]
    public void Evaluate_NoRuleFires_ReturnsEmptyNotNull()
    {
        var engine = new RecommendationEngine(new IRecommendationRule[] { new NeverFiresRule() });

        var results = engine.Evaluate(Stats(), Finding(DiagnosticConfidence.Confirmed));

        Assert.Empty(results);
    }

    [Fact]
    public void Constructor_EmptyRuleList_Throws()
    {
        Assert.Throws<ArgumentException>(() => new RecommendationEngine(Array.Empty<IRecommendationRule>()));
    }

    [Fact]
    public void DefaultRecommendationRules_HealthyResult_FiresExactlyTheNoActionRule()
    {
        var engine = new RecommendationEngine(DefaultRecommendationRules.Create());

        var results = engine.Evaluate(
            Stats(lossPercent: 0, medianMs: 20, jitterMs: 2),
            Finding(DiagnosticConfidence.Healthy));

        Assert.Single(results);
        Assert.Equal("recommend.healthy-no-action", results[0].Descriptor.Id);
    }

    [Fact]
    public void DefaultRecommendationRules_HighLossAndHighJitter_FiresBothRelevantRules()
    {
        var engine = new RecommendationEngine(DefaultRecommendationRules.Create());

        var results = engine.Evaluate(
            Stats(lossPercent: 10, medianMs: 20, jitterMs: 50),
            Finding(DiagnosticConfidence.Probable));

        Assert.Contains(results, r => r.Descriptor.Id == "recommend.packet-loss-investigate");
        Assert.Contains(results, r => r.Descriptor.Id == "recommend.high-jitter-bufferbloat-followup");
        Assert.DoesNotContain(results, r => r.Descriptor.Id == "recommend.healthy-no-action");
    }

    [Fact]
    public void DefaultRecommendationRules_HypothesisConfidence_FiresOnlyInsufficientSamples()
    {
        var engine = new RecommendationEngine(DefaultRecommendationRules.Create());

        var results = engine.Evaluate(
            Stats(sampleCount: 3, lossPercent: 50, medianMs: 500, jitterMs: 100),
            Finding(DiagnosticConfidence.Hypothesis));

        Assert.Single(results);
        Assert.Equal("recommend.insufficient-samples", results[0].Descriptor.Id);
    }
}
