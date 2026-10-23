using NetPulse.Domain.Recommendations;

namespace NetPulse.Application.UseCases;

/// <summary>
/// Thin adapter between this layer's <see cref="DiagnosticRunResult"/> (one per target) and
/// the pure Domain <see cref="RecommendationEngine"/>. Contains no rule logic itself - that
/// lives in Domain so it can be unit-tested without any Application/Infrastructure
/// dependency.
/// </summary>
public sealed class GenerateRecommendationsUseCase
{
    private readonly RecommendationEngine _engine;

    public GenerateRecommendationsUseCase(RecommendationEngine engine)
    {
        _engine = engine;
    }

    public IReadOnlyList<TriggeredRecommendation> Run(IReadOnlyList<DiagnosticRunResult> diagnosticResults)
    {
        var recommendations = new List<TriggeredRecommendation>();

        foreach (var result in diagnosticResults)
        {
            var evidence = new NetworkEvidence(result.Statistics, result.Finding, result.TcpCheck, result.DnsCheck);
            recommendations.AddRange(_engine.Evaluate(evidence));
        }

        return recommendations;
    }
}
