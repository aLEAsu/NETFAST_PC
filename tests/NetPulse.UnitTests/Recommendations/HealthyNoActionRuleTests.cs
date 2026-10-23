using NetPulse.Domain.Diagnostics;
using NetPulse.Domain.Recommendations.Rules;
using static NetPulse.UnitTests.Recommendations.RecommendationRuleTestHelpers;

namespace NetPulse.UnitTests.Recommendations;

public class HealthyNoActionRuleTests
{
    private readonly HealthyNoActionRule _rule = new();

    [Fact]
    public void Evaluate_HealthyConfidence_Fires()
    {
        var match = _rule.Evaluate(Stats(), Finding(DiagnosticConfidence.Healthy));

        Assert.NotNull(match);
        Assert.Equal("recommend.healthy-no-action", match!.Descriptor.Id);
    }

    [Theory]
    [InlineData(DiagnosticConfidence.Confirmed)]
    [InlineData(DiagnosticConfidence.Probable)]
    [InlineData(DiagnosticConfidence.Hypothesis)]
    [InlineData(DiagnosticConfidence.ExternalLimitation)]
    public void Evaluate_AnyNonHealthyConfidence_DoesNotFire(DiagnosticConfidence confidence)
    {
        var match = _rule.Evaluate(Stats(), Finding(confidence));

        Assert.Null(match);
    }

    [Fact]
    public void Evaluate_ExplicitlyProposesNoChange()
    {
        var match = _rule.Evaluate(Stats(), Finding(DiagnosticConfidence.Healthy));

        Assert.Equal("No change recommended", match!.Descriptor.Title);
        Assert.Empty(match.Descriptor.RequiredPermissions);
    }
}
