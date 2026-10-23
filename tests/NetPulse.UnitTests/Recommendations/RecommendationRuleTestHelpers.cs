using NetPulse.Domain.Diagnostics;
using NetPulse.Domain.Network;

namespace NetPulse.UnitTests.Recommendations;

internal static class RecommendationRuleTestHelpers
{
    public static LatencyStatistics Stats(
        string target = "1.1.1.1",
        int sampleCount = 20,
        int respondedCount = 20,
        double lossPercent = 0,
        double medianMs = 20,
        double jitterMs = 2) => new(
        Target: target,
        SampleCount: sampleCount,
        RespondedCount: respondedCount,
        PacketLossPercent: lossPercent,
        Min: TimeSpan.FromMilliseconds(Math.Max(0, medianMs - jitterMs)),
        Median: TimeSpan.FromMilliseconds(medianMs),
        P95: TimeSpan.FromMilliseconds(medianMs + jitterMs),
        Max: TimeSpan.FromMilliseconds(medianMs + jitterMs),
        JitterMeanAbsoluteDeviation: TimeSpan.FromMilliseconds(jitterMs),
        WindowStart: DateTimeOffset.UtcNow,
        WindowEnd: DateTimeOffset.UtcNow);

    public static DiagnosticFinding Finding(DiagnosticConfidence confidence, string id = "test.finding") =>
        new(id, "test summary", confidence, "test evidence", Array.Empty<string>());
}
