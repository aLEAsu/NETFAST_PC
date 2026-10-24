using NetPulse.Domain.Diagnostics;
using NetPulse.Domain.Recommendations.Rules;
using static NetPulse.UnitTests.Recommendations.RecommendationRuleTestHelpers;

namespace NetPulse.UnitTests.Recommendations;

public class PacketLossInvestigateRuleTests
{
    private readonly PacketLossInvestigateRule _rule = new();

    [Fact]
    public void Evaluate_LossAtOrAboveThreshold_Fires()
    {
        var match = _rule.Evaluate(Evidence(Stats(lossPercent: 2.0), Finding(DiagnosticConfidence.Probable)));

        Assert.NotNull(match);
        Assert.Equal("recommend.packet-loss-investigate", match!.Descriptor.Id);
    }

    [Fact]
    public void Evaluate_LossBelowThreshold_DoesNotFire()
    {
        var match = _rule.Evaluate(Evidence(Stats(lossPercent: 1.9), Finding(DiagnosticConfidence.Probable)));

        Assert.Null(match);
    }

    [Fact]
    public void Evaluate_HypothesisConfidence_NeverFiresRegardlessOfLoss()
    {
        var match = _rule.Evaluate(Evidence(Stats(lossPercent: 100), Finding(DiagnosticConfidence.Hypothesis)));

        Assert.Null(match);
    }

    [Fact]
    public void Evaluate_NeverRecommendsAWindowsSettingChange()
    {
        // The rule must only suggest external investigation, per the brief's requirement
        // that loss not be attributed to the PC before router/ISP causes are ruled out.
        var match = _rule.Evaluate(Evidence(Stats(lossPercent: 10), Finding(DiagnosticConfidence.Probable)));

        Assert.Empty(match!.Descriptor.RequiredPermissions);
        Assert.Contains("No aplica", match.Descriptor.RollbackMethod);
    }
}
