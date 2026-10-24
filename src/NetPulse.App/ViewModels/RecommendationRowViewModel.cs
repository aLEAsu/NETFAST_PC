using NetPulse.Domain.Recommendations;

namespace NetPulse.App.ViewModels;

public sealed class RecommendationRowViewModel
{
    /// <summary>
    /// The one recommendation Id this build knows how to actually apply - the link between
    /// Phase 4's recommendation engine and Phase 5's privileged action is by this naming
    /// convention, not an object reference, because Domain cannot depend on PrivilegedAgent
    /// (see SlowDnsResolutionRule's own doc comment).
    /// </summary>
    public const string ApplyableDnsRecommendationId = "recommend.slow-dns-resolution";

    public RecommendationRowViewModel(TriggeredRecommendation recommendation)
    {
        var descriptor = recommendation.Descriptor;

        Target = recommendation.Target;
        // Title/Explanation/NextStep/Limitations come straight from the Domain rule
        // descriptors - now in Spanish there too; see FindingRowViewModel's equivalent note.
        Title = descriptor.Title;
        Risk = SpanishDisplayNames.Risk(descriptor.Risk);
        BasisConfidence = SpanishDisplayNames.Confidence(recommendation.BasisConfidence);
        Explanation = recommendation.TriggerExplanation;
        NextStep = descriptor.VerificationMethod;
        Limitations = descriptor.KnownLimitations.Length > 0 ? string.Join(" | ", descriptor.KnownLimitations) : "-";
        CanApply = descriptor.Id == ApplyableDnsRecommendationId;
    }

    public string Target { get; }
    public string Title { get; }
    public string Risk { get; }
    public string BasisConfidence { get; }
    public string Explanation { get; }
    public string NextStep { get; }
    public string Limitations { get; }

    /// <summary>True only for the one recommendation this build can actually offer to apply.</summary>
    public bool CanApply { get; }
}
