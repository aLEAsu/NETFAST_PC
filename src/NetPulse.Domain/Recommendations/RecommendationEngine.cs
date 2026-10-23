namespace NetPulse.Domain.Recommendations;

/// <summary>
/// Runs every configured rule against one target's diagnostic evidence and collects
/// whichever ones fire. Deliberately does not rank, deduplicate across targets, or decide
/// what to do with the result - that is presentation/orchestration concern, kept out of
/// this pure evaluation step.
/// </summary>
public sealed class RecommendationEngine
{
    private readonly IReadOnlyList<IRecommendationRule> _rules;

    public RecommendationEngine(IReadOnlyList<IRecommendationRule> rules)
    {
        if (rules.Count == 0)
            throw new ArgumentException("At least one rule is required.", nameof(rules));

        _rules = rules;
    }

    public IReadOnlyList<TriggeredRecommendation> Evaluate(NetworkEvidence evidence)
    {
        var triggered = new List<TriggeredRecommendation>();

        foreach (var rule in _rules)
        {
            var match = rule.Evaluate(evidence);
            if (match is not null)
                triggered.Add(match);
        }

        return triggered;
    }
}
