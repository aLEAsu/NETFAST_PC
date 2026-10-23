using NetPulse.Application.UseCases;
using NetPulse.Domain.Diagnostics;
using NetPulse.Domain.Recommendations;
using static NetPulse.UnitTests.Recommendations.RecommendationRuleTestHelpers;

namespace NetPulse.UnitTests.UseCases;

public class GenerateRecommendationsUseCaseTests
{
    [Fact]
    public void Run_MultipleTargets_AggregatesRecommendationsAcrossAll()
    {
        var engine = new RecommendationEngine(DefaultRecommendationRules.Create());
        var useCase = new GenerateRecommendationsUseCase(engine);

        var results = new[]
        {
            new DiagnosticRunResult(Stats(target: "1.1.1.1"), Finding(DiagnosticConfidence.Healthy)),
            new DiagnosticRunResult(Stats(target: "8.8.8.8", lossPercent: 10), Finding(DiagnosticConfidence.Probable)),
        };

        var recommendations = useCase.Run(results);

        Assert.Contains(recommendations, r => r.Target == "1.1.1.1" && r.Descriptor.Id == "recommend.healthy-no-action");
        Assert.Contains(recommendations, r => r.Target == "8.8.8.8" && r.Descriptor.Id == "recommend.packet-loss-investigate");
    }

    [Fact]
    public void Run_NoResults_ReturnsEmpty()
    {
        var engine = new RecommendationEngine(DefaultRecommendationRules.Create());
        var useCase = new GenerateRecommendationsUseCase(engine);

        var recommendations = useCase.Run(Array.Empty<DiagnosticRunResult>());

        Assert.Empty(recommendations);
    }
}
