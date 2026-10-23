using NetPulse.Domain.Optimization;
using NetPulse.Domain.Profiles;
using NetPulse.Domain.Recommendations;

namespace NetPulse.UnitTests.Profiles;

public class DiagnosticProfileSettingsTests
{
    private static TriggeredRecommendation Recommendation(string id) => new(
        "1.1.1.1",
        new OptimizationDescriptor(id, "t", "t", "t", Array.Empty<string>(), OptimizationRisk.Low,
            Array.Empty<string>(), "t", "t", "t", "t", Array.Empty<string>(), Array.Empty<string>()),
        "explanation",
        NetPulse.Domain.Diagnostics.DiagnosticConfidence.Probable);

    [Fact]
    public void Prioritize_PutsMatchingIdsFirst_WithoutDroppingAnyItem()
    {
        var settings = new DiagnosticProfileSettings(
            DiagnosticProfile.Gaming, "Gaming", "focus", 30, TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(1),
            new[] { "recommend.high-jitter-bufferbloat-followup" });

        var input = new[]
        {
            Recommendation("recommend.packet-loss-investigate"),
            Recommendation("recommend.high-jitter-bufferbloat-followup"),
            Recommendation("recommend.healthy-no-action"),
        };

        var result = settings.Prioritize(input);

        Assert.Equal(3, result.Count);
        Assert.Equal("recommend.high-jitter-bufferbloat-followup", result[0].Descriptor.Id);
        Assert.Equal(new[] { "recommend.packet-loss-investigate", "recommend.high-jitter-bufferbloat-followup", "recommend.healthy-no-action" },
            input.Select(r => r.Descriptor.Id)); // original list untouched
    }

    [Fact]
    public void Prioritize_NoPriorityIdsMatch_PreservesOriginalOrder()
    {
        var settings = new DiagnosticProfileSettings(
            DiagnosticProfile.General, "General", "focus", 20, TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(2),
            Array.Empty<string>());

        var input = new[]
        {
            Recommendation("a"),
            Recommendation("b"),
            Recommendation("c"),
        };

        var result = settings.Prioritize(input);

        Assert.Equal(new[] { "a", "b", "c" }, result.Select(r => r.Descriptor.Id));
    }

    [Fact]
    public void Prioritize_EmptyInput_ReturnsEmpty()
    {
        var settings = DiagnosticProfileCatalog.Get(DiagnosticProfile.General);

        var result = settings.Prioritize(Array.Empty<TriggeredRecommendation>());

        Assert.Empty(result);
    }
}
