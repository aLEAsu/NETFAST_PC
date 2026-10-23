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

    private static TcpConnectSample TcpOk() =>
        new("1.1.1.1", 443, DateTimeOffset.UtcNow, Connected: true, ConnectTime: TimeSpan.FromMilliseconds(15), Error: null);

    private static TcpConnectSample TcpFailed() =>
        new("1.1.1.1", 443, DateTimeOffset.UtcNow, Connected: false, ConnectTime: null, Error: "simulated failure");

    [Fact]
    public void Classify_NoResponsesAndTcpAlsoFails_IsProbableNotConfirmed()
    {
        var finding = ConnectionDiagnosticUseCase.Classify(Stats(sampleCount: 20, respondedCount: 0, lossPercent: 100), TcpFailed());

        Assert.Equal(DiagnosticConfidence.Probable, finding.Confidence);
        Assert.Equal("connectivity.no-response", finding.Id);
        Assert.NotEmpty(finding.Limitations);
    }

    [Fact]
    public void Classify_NoIcmpResponseButTcpConnects_IsHealthyNotProbable()
    {
        // The whole point of adding the TCP confirmation: ICMP silence alone must not be
        // reported as a connectivity problem when the target is demonstrably reachable.
        var finding = ConnectionDiagnosticUseCase.Classify(Stats(sampleCount: 20, respondedCount: 0, lossPercent: 100), TcpOk());

        Assert.Equal(DiagnosticConfidence.Healthy, finding.Confidence);
        Assert.Equal("connectivity.icmp-filtered-app-reachable", finding.Id);
    }

    [Fact]
    public void Classify_TooFewSamples_IsHypothesisRegardlessOfLoss()
    {
        var finding = ConnectionDiagnosticUseCase.Classify(Stats(sampleCount: 5, respondedCount: 5, lossPercent: 0), TcpOk());

        Assert.Equal(DiagnosticConfidence.Hypothesis, finding.Confidence);
    }

    [Fact]
    public void Classify_SignificantPacketLoss_IsProbable()
    {
        var finding = ConnectionDiagnosticUseCase.Classify(Stats(sampleCount: 20, respondedCount: 18, lossPercent: 10), TcpOk());

        Assert.Equal(DiagnosticConfidence.Probable, finding.Confidence);
        Assert.Equal("connectivity.packet-loss", finding.Id);
    }

    [Fact]
    public void Classify_HighJitter_IsProbable()
    {
        var finding = ConnectionDiagnosticUseCase.Classify(Stats(sampleCount: 20, respondedCount: 20, lossPercent: 0, jitterMs: 45), TcpOk());

        Assert.Equal(DiagnosticConfidence.Probable, finding.Confidence);
        Assert.Equal("connectivity.high-jitter", finding.Id);
    }

    [Fact]
    public void Classify_StableLowLatency_IsHealthy()
    {
        var finding = ConnectionDiagnosticUseCase.Classify(Stats(sampleCount: 20, respondedCount: 20, lossPercent: 0, jitterMs: 2), TcpOk());

        Assert.Equal(DiagnosticConfidence.Healthy, finding.Confidence);
    }
}
