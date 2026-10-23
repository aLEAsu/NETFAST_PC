using NetPulse.Domain.Diagnostics;
using NetPulse.Domain.Recommendations.Rules;
using static NetPulse.UnitTests.Recommendations.RecommendationRuleTestHelpers;

namespace NetPulse.UnitTests.Recommendations;

public class InsufficientSamplesRuleTests
{
    private readonly InsufficientSamplesRule _rule = new();

    [Fact]
    public void Evaluate_HypothesisConfidence_Fires()
    {
        var match = _rule.Evaluate(Stats(sampleCount: 5), Finding(DiagnosticConfidence.Hypothesis));

        Assert.NotNull(match);
        Assert.Equal("recommend.insufficient-samples", match!.Descriptor.Id);
        Assert.Contains("1.1.1.1", match.TriggerExplanation);
    }

    [Theory]
    [InlineData(DiagnosticConfidence.Confirmed)]
    [InlineData(DiagnosticConfidence.Probable)]
    [InlineData(DiagnosticConfidence.ExternalLimitation)]
    [InlineData(DiagnosticConfidence.Healthy)]
    public void Evaluate_AnyOtherConfidence_DoesNotFire(DiagnosticConfidence confidence)
    {
        var match = _rule.Evaluate(Stats(), Finding(confidence));

        Assert.Null(match);
    }

    [Fact]
    public void Evaluate_NeverProposesAChange()
    {
        var match = _rule.Evaluate(Stats(sampleCount: 5), Finding(DiagnosticConfidence.Hypothesis));

        Assert.Contains("Not applicable", match!.Descriptor.RollbackMethod);
        Assert.Empty(match.Descriptor.RequiredPermissions);
    }
}
