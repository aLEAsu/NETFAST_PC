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
        var useCase = new ConnectionDiagnosticUseCase(probeService, historyRepository);

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
    public async Task RunAsync_NoTargets_Throws()
    {
        var useCase = new ConnectionDiagnosticUseCase(new FakeProbeService(), new FakeHistoryRepository());
        var options = new DiagnosticOptions(Targets: Array.Empty<string>());

        await Assert.ThrowsAsync<ArgumentException>(() => useCase.RunAsync(options, CancellationToken.None));
    }

    [Fact]
    public async Task RunAsync_CancelledMidRun_StopsProbing()
    {
        var probeService = new FakeProbeService();
        var historyRepository = new FakeHistoryRepository();
        var useCase = new ConnectionDiagnosticUseCase(probeService, historyRepository);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var options = new DiagnosticOptions(Targets: new[] { "1.1.1.1" }, SampleCount: 10);

        await Assert.ThrowsAsync<OperationCanceledException>(() => useCase.RunAsync(options, cts.Token));
        Assert.Equal(0, probeService.CallCount);
    }
}
