using NetPulse.Domain.Diagnostics;
using NetPulse.Domain.Recommendations.Rules;
using static NetPulse.UnitTests.Recommendations.RecommendationRuleTestHelpers;

namespace NetPulse.UnitTests.Recommendations;

public class HighJitterBufferbloatFollowUpRuleTests
{
    private readonly HighJitterBufferbloatFollowUpRule _rule = new();

    [Fact]
    public void Evaluate_JitterAboveThreshold_Fires()
    {
        var match = _rule.Evaluate(Stats(jitterMs: 31), Finding(DiagnosticConfidence.Probable));

        Assert.NotNull(match);
        Assert.Equal("recommend.high-jitter-bufferbloat-followup", match!.Descriptor.Id);
    }

    [Fact]
    public void Evaluate_JitterAtOrBelowThreshold_DoesNotFire()
    {
        var match = _rule.Evaluate(Stats(jitterMs: 30), Finding(DiagnosticConfidence.Probable));

        Assert.Null(match);
    }

    [Fact]
    public void Evaluate_HypothesisConfidence_NeverFires()
    {
        var match = _rule.Evaluate(Stats(jitterMs: 100), Finding(DiagnosticConfidence.Hypothesis));

        Assert.Null(match);
    }

    [Fact]
    public void Evaluate_OnlyEverRecommendsAFollowUpTest_NeverAChange()
    {
        var match = _rule.Evaluate(Stats(jitterMs: 50), Finding(DiagnosticConfidence.Probable));

        Assert.Contains("bufferbloat", match!.Descriptor.Title, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(match.Descriptor.RequiredPermissions);
    }
}
