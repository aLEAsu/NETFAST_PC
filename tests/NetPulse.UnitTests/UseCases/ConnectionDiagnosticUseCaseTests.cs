using NetPulse.Application.Ports;
using NetPulse.Application.UseCases;
using NetPulse.Domain.Network;

namespace NetPulse.UnitTests.UseCases;

public class ConnectionDiagnosticUseCaseTests
{
    private sealed class FakeProbeService : ILatencyProbeService
    {
        private int _callCount;
        public int CallCount => _callCount;

        public Task<LatencySample> ProbeOnceAsync(string target, TimeSpan timeout, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);
            return Task.FromResult(new LatencySample(target, DateTimeOffset.UtcNow, Responded: true, RoundTripTime: TimeSpan.FromMilliseconds(15)));
        }
    }

    private sealed class FakeTcpProbeService : ITcpConnectProbeService
    {
        private int _callCount;
        public int CallCount => _callCount;

        public Task<TcpConnectSample> ProbeOnceAsync(string target, int port, TimeSpan timeout, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);
            return Task.FromResult(new TcpConnectSample(target, port, DateTimeOffset.UtcNow, Connected: true, TimeSpan.FromMilliseconds(10), Error: null));
        }
    }

    private sealed class FakeDnsProbeService : IDnsResolutionProbeService
    {
        private int _callCount;
        public int CallCount => _callCount;

        public Task<DnsResolutionSample> ProbeOnceAsync(string hostname, TimeSpan timeout, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);
            return Task.FromResult(new DnsResolutionSample(hostname, DateTimeOffset.UtcNow, Resolved: true, TimeSpan.FromMilliseconds(5), new[] { "1.2.3.4" }, Error: null));
        }
    }

    private sealed class FakeHistoryRepository : IDiagnosticHistoryRepository
    {
        public readonly List<LatencyStatistics> Saved = new();

        public Task SaveAsync(LatencyStatistics statistics, CancellationToken cancellationToken)
        {
            Saved.Add(statistics);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<LatencyStatistics>> GetRecentAsync(string target, int count, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<LatencyStatistics>>(Saved.Where(s => s.Target == target).Take(count).ToList());
    }

    [Fact]
    public async Task RunAsync_ProbesEachTargetTheConfiguredNumberOfTimes()
    {
        var probeService = new FakeProbeService();
        var historyRepository = new FakeHistoryRepository();
        var useCase = new ConnectionDiagnosticUseCase(probeService, new FakeTcpProbeService(), new FakeDnsProbeService(), historyRepository);

        var options = new DiagnosticOptions(
            Targets: new[] { "1.1.1.1", "8.8.8.8" },
            SampleCount: 5,
            IntervalBetweenSamples: TimeSpan.Zero);

        var results = await useCase.RunAsync(options, CancellationToken.None);

        Assert.Equal(2, results.Count);
        Assert.Equal(10, probeService.CallCount);
        Assert.Equal(2, historyRepository.Saved.Count);
        Assert.All(results, r => Assert.Equal(5, r.Statistics.SampleCount));
    }

    [Fact]
    public async Task RunAsync_IPLiteralTargets_ProbeTcpOnceButSkipDnsResolution()
    {
        var tcpProbeService = new FakeTcpProbeService();
        var dnsProbeService = new FakeDnsProbeService();
        var useCase = new ConnectionDiagnosticUseCase(new FakeProbeService(), tcpProbeService, dnsProbeService, new FakeHistoryRepository());

        var options = new DiagnosticOptions(Targets: new[] { "1.1.1.1", "8.8.8.8" }, SampleCount: 1, IntervalBetweenSamples: TimeSpan.Zero);

        var results = await useCase.RunAsync(options, CancellationToken.None);

        Assert.Equal(2, tcpProbeService.CallCount); // one TCP confirmation attempt per target
        Assert.Equal(0, dnsProbeService.CallCount); // IP literals have nothing to resolve
        Assert.All(results, r => Assert.False(r.DnsCheck.IsApplicable));
    }

    [Fact]
    public async Task RunAsync_HostnameTarget_ProbesDnsResolutionOnce()
    {
        var dnsProbeService = new FakeDnsProbeService();
        var useCase = new ConnectionDiagnosticUseCase(new FakeProbeService(), new FakeTcpProbeService(), dnsProbeService, new FakeHistoryRepository());

        var options = new DiagnosticOptions(Targets: new[] { "example.com" }, SampleCount: 1, IntervalBetweenSamples: TimeSpan.Zero);

        var results = await useCase.RunAsync(options, CancellationToken.None);

        Assert.Equal(1, dnsProbeService.CallCount);
        Assert.True(results[0].DnsCheck.IsApplicable);
    }

    [Fact]
    public async Task RunAsync_NoTargets_Throws()
    {
        var useCase = new ConnectionDiagnosticUseCase(new FakeProbeService(), new FakeTcpProbeService(), new FakeDnsProbeService(), new FakeHistoryRepository());
        var options = new DiagnosticOptions(Targets: Array.Empty<string>());

        await Assert.ThrowsAsync<ArgumentException>(() => useCase.RunAsync(options, CancellationToken.None));
    }

    [Fact]
    public async Task RunAsync_CancelledMidRun_StopsProbing()
    {
        var probeService = new FakeProbeService();
        var historyRepository = new FakeHistoryRepository();
        var useCase = new ConnectionDiagnosticUseCase(probeService, new FakeTcpProbeService(), new FakeDnsProbeService(), historyRepository);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var options = new DiagnosticOptions(Targets: new[] { "1.1.1.1" }, SampleCount: 10);

        await Assert.ThrowsAsync<OperationCanceledException>(() => useCase.RunAsync(options, cts.Token));
        Assert.Equal(0, probeService.CallCount);
    }
}
