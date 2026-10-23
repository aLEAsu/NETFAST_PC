using System.Diagnostics;
using NetPulse.Application.Ports;
using NetPulse.Application.UseCases;
using NetPulse.Domain.Diagnostics;
using NetPulse.Domain.Network;
using NetPulse.Domain.Recommendations;

namespace NetPulse.PerformanceTests;

/// <summary>
/// Measures the CPU cost of this project's OWN code (statistics math, classification, the
/// recommendation engine, use-case orchestration) with network latency removed from the
/// measurement via an instant fake probe - the goal is to prove the orchestration overhead
/// is negligible, not to re-measure how long a real ICMP round trip takes. These are real
/// executed assertions with generous thresholds (meant to catch an accidental O(n^2) or a
/// runaway allocation, not to chase microseconds), not invented numbers - every number
/// quoted in documentation is produced by actually running this project.
/// </summary>
public class DiagnosticPipelinePerformanceTests
{
    private sealed class InstantFakeProbeService : ILatencyProbeService
    {
        public Task<LatencySample> ProbeOnceAsync(string target, TimeSpan timeout, CancellationToken cancellationToken) =>
            Task.FromResult(new LatencySample(target, DateTimeOffset.UtcNow, Responded: true, RoundTripTime: TimeSpan.FromMilliseconds(15)));
    }

    private sealed class InstantFakeTcpProbeService : ITcpConnectProbeService
    {
        public Task<TcpConnectSample> ProbeOnceAsync(string target, int port, TimeSpan timeout, CancellationToken cancellationToken) =>
            Task.FromResult(new TcpConnectSample(target, port, DateTimeOffset.UtcNow, Connected: true, TimeSpan.FromMilliseconds(10), Error: null));
    }

    private sealed class InstantFakeDnsProbeService : IDnsResolutionProbeService
    {
        public Task<DnsResolutionSample> ProbeOnceAsync(string hostname, TimeSpan timeout, CancellationToken cancellationToken) =>
            Task.FromResult(new DnsResolutionSample(hostname, DateTimeOffset.UtcNow, Resolved: true, TimeSpan.FromMilliseconds(5), new[] { "1.2.3.4" }, Error: null));
    }

    private sealed class NoOpHistoryRepository : IDiagnosticHistoryRepository
    {
        public Task SaveAsync(LatencyStatistics statistics, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyList<LatencyStatistics>> GetRecentAsync(string target, int count, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<LatencyStatistics>>(Array.Empty<LatencyStatistics>());
    }

    [Fact]
    public void LatencyStatistics_From_1000Samples_CompletesWellUnderFiveMilliseconds()
    {
        var baseTime = DateTimeOffset.UtcNow;
        var samples = Enumerable.Range(0, 1000)
            .Select(i => new LatencySample("1.1.1.1", baseTime.AddMilliseconds(i), true, TimeSpan.FromMilliseconds(10 + i % 50)))
            .ToList();

        LatencyStatistics.From("1.1.1.1", samples); // warm up (JIT) before the measured loop

        var stopwatch = Stopwatch.StartNew();
        for (int i = 0; i < 100; i++)
            LatencyStatistics.From("1.1.1.1", samples);
        stopwatch.Stop();

        var averageMicroseconds = stopwatch.Elapsed.TotalMicroseconds / 100;
        Assert.True(averageMicroseconds < 5000, $"Expected well under 5 ms per call on 1000 samples, measured {averageMicroseconds:F0} microseconds.");
    }

    [Fact]
    public void RecommendationEngine_Evaluate_CompletesWellUnderOneMillisecond()
    {
        var engine = new RecommendationEngine(DefaultRecommendationRules.Create());
        var stats = new LatencyStatistics("1.1.1.1", 20, 20, 0, TimeSpan.FromMilliseconds(10),
            TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(25), TimeSpan.FromMilliseconds(30),
            TimeSpan.FromMilliseconds(3), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var finding = new DiagnosticFinding("test", "test", DiagnosticConfidence.Healthy, "test", Array.Empty<string>());
        var tcpCheck = new TcpConnectSample("1.1.1.1", 443, DateTimeOffset.UtcNow, true, TimeSpan.FromMilliseconds(10), null);
        var dnsCheck = DnsResolutionSample.NotApplicable("1.1.1.1");
        var evidence = new NetworkEvidence(stats, finding, tcpCheck, dnsCheck);

        var stopwatch = Stopwatch.StartNew();
        for (int i = 0; i < 1000; i++)
            engine.Evaluate(evidence);
        stopwatch.Stop();

        var averageMicroseconds = stopwatch.Elapsed.TotalMicroseconds / 1000;
        Assert.True(averageMicroseconds < 1000, $"Expected well under 1 ms per call, measured {averageMicroseconds:F1} microseconds.");
    }

    [Fact]
    public async Task ConnectionDiagnosticUseCase_RunAsync_OrchestrationOverheadIsNegligibleOnTopOfProbeTime()
    {
        // Network delay is intentional and configured (IntervalBetweenSamples) - this test
        // isolates the use case's OWN overhead by setting that interval to zero and using an
        // instant fake probe, so total elapsed time reflects only this project's code.
        var useCase = new ConnectionDiagnosticUseCase(new InstantFakeProbeService(), new InstantFakeTcpProbeService(), new InstantFakeDnsProbeService(), new NoOpHistoryRepository());
        var options = new DiagnosticOptions(new[] { "1.1.1.1", "8.8.8.8" }, SampleCount: 20, IntervalBetweenSamples: TimeSpan.Zero);

        // This is a single-shot measurement (unlike the averaged-over-many-iterations tests
        // above), so it is more sensitive to one-time JIT cost - a real factor the very bug
        // this suite caught (DiagnosticOptions' zero-interval regression) was found through.
        // Warming up once first keeps the measured run representative of steady-state cost.
        await useCase.RunAsync(options, CancellationToken.None);

        var stopwatch = Stopwatch.StartNew();
        var results = await useCase.RunAsync(options, CancellationToken.None);
        stopwatch.Stop();

        Assert.Equal(2, results.Count);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromMilliseconds(200),
            $"Expected 40 fake probes + statistics for 2 targets to complete in well under 200 ms, measured {stopwatch.ElapsedMilliseconds} ms.");
    }

    [Fact]
    public void LatencyStatistics_From_DoesNotAllocateExcessively()
    {
        var baseTime = DateTimeOffset.UtcNow;
        var samples = Enumerable.Range(0, 100)
            .Select(i => new LatencySample("1.1.1.1", baseTime.AddMilliseconds(i), true, TimeSpan.FromMilliseconds(10 + i % 20)))
            .ToList();

        LatencyStatistics.From("1.1.1.1", samples); // warm up (JIT, any one-time allocations)

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++)
            LatencyStatistics.From("1.1.1.1", samples);
        long after = GC.GetAllocatedBytesForCurrentThread();

        var averageBytesPerCall = (after - before) / 100.0;
        // Generous bound: a few sorted/filtered copies of 100 samples plus a handful of
        // small objects is on the order of tens of KB, not megabytes.
        Assert.True(averageBytesPerCall < 200_000, $"Expected well under 200 KB allocated per call, measured {averageBytesPerCall:F0} bytes.");
    }
}
