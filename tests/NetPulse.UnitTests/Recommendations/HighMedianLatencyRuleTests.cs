using NetPulse.Domain.Diagnostics;
using NetPulse.Domain.Recommendations.Rules;
using static NetPulse.UnitTests.Recommendations.RecommendationRuleTestHelpers;

namespace NetPulse.UnitTests.Recommendations;

public class HighMedianLatencyRuleTests
{
    private readonly HighMedianLatencyRule _rule = new();

    [Fact]
    public void Evaluate_MedianAboveThreshold_Fires()
    {
        var match = _rule.Evaluate(Evidence(Stats(medianMs: 101, jitterMs: 2), Finding(DiagnosticConfidence.Probable)));

        Assert.NotNull(match);
        Assert.Equal("recommend.high-median-latency", match!.Descriptor.Id);
    }

    [Fact]
    public void Evaluate_MedianAtOrBelowThreshold_DoesNotFire()
    {
        var match = _rule.Evaluate(Evidence(Stats(medianMs: 100, jitterMs: 2), Finding(DiagnosticConfidence.Probable)));

        Assert.Null(match);
    }

    [Fact]
    public void Evaluate_HypothesisConfidence_NeverFires()
    {
        var match = _rule.Evaluate(Evidence(Stats(medianMs: 500), Finding(DiagnosticConfidence.Hypothesis)));

        Assert.Null(match);
    }

    [Fact]
    public void Evaluate_NeverBlamesThePcForDistanceLatency()
    {
        var match = _rule.Evaluate(Evidence(Stats(medianMs: 300), Finding(DiagnosticConfidence.Healthy)));

        Assert.Contains("distance", match!.Descriptor.Title, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(match.Descriptor.RequiredPermissions);
    }
}
