using NetPulse.Domain.Recommendations;

namespace NetPulse.App.ViewModels;

public sealed class RecommendationRowViewModel
{
    public RecommendationRowViewModel(TriggeredRecommendation recommendation)
    {
        var descriptor = recommendation.Descriptor;

        Target = recommendation.Target;
        Title = descriptor.Title;
        Risk = descriptor.Risk.ToString();
        BasisConfidence = recommendation.BasisConfidence.ToString();
        Explanation = recommendation.TriggerExplanation;
        NextStep = descriptor.VerificationMethod;
        Limitations = descriptor.KnownLimitations.Length > 0 ? string.Join(" | ", descriptor.KnownLimitations) : "-";
    }

    public string Target { get; }
    public string Title { get; }
    public string Risk { get; }
    public string BasisConfidence { get; }
    public string Explanation { get; }
    public string NextStep { get; }
    public string Limitations { get; }
}
