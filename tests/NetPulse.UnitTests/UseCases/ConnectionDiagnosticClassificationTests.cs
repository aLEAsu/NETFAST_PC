using NetPulse.Application.UseCases;
using NetPulse.Domain.Diagnostics;
using NetPulse.Domain.Network;

namespace NetPulse.UnitTests.UseCases;

public class ConnectionDiagnosticClassificationTests
{
    private static LatencyStatistics Stats(
        int sampleCount, int respondedCount, double lossPercent,
        double medianMs = 20, double jitterMs = 2) =>
        new(
            Target: "1.1.1.1",
            SampleCount: sampleCount,
            RespondedCount: respondedCount,
            PacketLossPercent: lossPercent,
            Min: TimeSpan.FromMilliseconds(medianMs - jitterMs),
            Median: TimeSpan.FromMilliseconds(medianMs),
            P95: TimeSpan.FromMilliseconds(medianMs + jitterMs),
            Max: TimeSpan.FromMilliseconds(medianMs + jitterMs),
            JitterMeanAbsoluteDeviation: TimeSpan.FromMilliseconds(jitterMs),
            WindowStart: DateTimeOffset.UtcNow,
            WindowEnd: DateTimeOffset.UtcNow);

    [Fact]
    public void Classify_NoResponses_IsProbableNotConfirmed()
    {
        var finding = ConnectionDiagnosticUseCase.Classify(Stats(sampleCount: 20, respondedCount: 0, lossPercent: 100));

        Assert.Equal(DiagnosticConfidence.Probable, finding.Confidence);
        Assert.NotEmpty(finding.Limitations);
    }

    [Fact]
    public void Classify_TooFewSamples_IsHypothesisRegardlessOfLoss()
    {
        var finding = ConnectionDiagnosticUseCase.Classify(Stats(sampleCount: 5, respondedCount: 5, lossPercent: 0));

        Assert.Equal(DiagnosticConfidence.Hypothesis, finding.Confidence);
    }

    [Fact]
    public void Classify_SignificantPacketLoss_IsProbable()
    {
        var finding = ConnectionDiagnosticUseCase.Classify(Stats(sampleCount: 20, respondedCount: 18, lossPercent: 10));

        Assert.Equal(DiagnosticConfidence.Probable, finding.Confidence);
        Assert.Equal("connectivity.packet-loss", finding.Id);
    }

    [Fact]
    public void Classify_HighJitter_IsProbable()
    {
        var finding = ConnectionDiagnosticUseCase.Classify(Stats(sampleCount: 20, respondedCount: 20, lossPercent: 0, jitterMs: 45));

        Assert.Equal(DiagnosticConfidence.Probable, finding.Confidence);
        Assert.Equal("connectivity.high-jitter", finding.Id);
    }

    [Fact]
    public void Classify_StableLowLatency_IsHealthy()
    {
        var finding = ConnectionDiagnosticUseCase.Classify(Stats(sampleCount: 20, respondedCount: 20, lossPercent: 0, jitterMs: 2));

        Assert.Equal(DiagnosticConfidence.Healthy, finding.Confidence);
    }
}
