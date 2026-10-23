using NetPulse.Domain.Diagnostics;
using NetPulse.Domain.Network;
using NetPulse.Domain.Recommendations;

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

    /// <summary>Neutral default: a successful TCP check and an inapplicable (IP-literal-style) DNS check.</summary>
    public static TcpConnectSample TcpOk(string target = "1.1.1.1", int port = 443) =>
        new(target, port, DateTimeOffset.UtcNow, Connected: true, ConnectTime: TimeSpan.FromMilliseconds(20), Error: null);

    public static TcpConnectSample TcpFailed(string target = "1.1.1.1", int port = 443) =>
        new(target, port, DateTimeOffset.UtcNow, Connected: false, ConnectTime: null, Error: "simulated failure");

    public static DnsResolutionSample DnsNotApplicable(string target = "1.1.1.1") =>
        DnsResolutionSample.NotApplicable(target);

    public static DnsResolutionSample DnsSlow(string hostname, double resolutionMs) =>
        new(hostname, DateTimeOffset.UtcNow, Resolved: true, TimeSpan.FromMilliseconds(resolutionMs), new[] { "1.2.3.4" }, Error: null);

    public static DnsResolutionSample DnsFailed(string hostname) =>
        new(hostname, DateTimeOffset.UtcNow, Resolved: false, ResolutionTime: null, Array.Empty<string>(), Error: "simulated resolution failure");

    /// <summary>
    /// Bundles evidence for rule evaluation, defaulting TCP/DNS to neutral values so existing
    /// latency-focused tests don't need to specify them unless they're the thing under test.
    /// </summary>
    public static NetworkEvidence Evidence(LatencyStatistics statistics, DiagnosticFinding finding, TcpConnectSample? tcpCheck = null, DnsResolutionSample? dnsCheck = null) =>
        new(statistics, finding, tcpCheck ?? TcpOk(statistics.Target), dnsCheck ?? DnsNotApplicable(statistics.Target));
}
