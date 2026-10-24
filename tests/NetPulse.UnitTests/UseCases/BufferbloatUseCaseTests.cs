using NetPulse.Application.Ports;
using NetPulse.Application.UseCases;
using NetPulse.Domain.Network;

namespace NetPulse.UnitTests.UseCases;

/// <summary>
/// Exercises BufferbloatUseCase entirely against fakes - no real network, no real load
/// against the internet. Per the project's constraint that only a human may trigger a real
/// run of this kind, this suite proves the comparison/validation logic works without ever
/// invoking the real CloudflareLoadGenerationService (see its own integration test, which
/// points at a local loopback listener instead of the real internet).
/// </summary>
public class BufferbloatUseCaseTests
{
    private sealed class FakeLatencyProbeService : ILatencyProbeService
    {
        public double RoundTripMilliseconds { get; set; } = 10;
        public int CallCount { get; private set; }

        public Task<LatencySample> ProbeOnceAsync(string target, TimeSpan timeout, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new LatencySample(target, DateTimeOffset.UtcNow, Responded: true, RoundTripTime: TimeSpan.FromMilliseconds(RoundTripMilliseconds)));
        }
    }

    private sealed class FakeLoadGenerationService : ILoadGenerationService
    {
        public LoadDirection? LastDirection { get; private set; }
        public TimeSpan? LastDuration { get; private set; }

        public async Task GenerateLoadAsync(LoadDirection direction, TimeSpan duration, CancellationToken cancellationToken)
        {
            LastDirection = direction;
            LastDuration = duration;
            try
            {
                await Task.Delay(duration, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Expected: the use case's own duration cap (or the caller) cancelled this -
                // a real implementation handles this the same way.
            }
        }
    }

    private static BufferbloatOptions FastOptions(LoadDirection direction = LoadDirection.Download) => new(
        Target: "1.1.1.1",
        Direction: direction,
        LoadDuration: TimeSpan.FromMilliseconds(300),
        RestSampleCount: 2,
        SampleInterval: TimeSpan.FromMilliseconds(20));

    [Fact]
    public async Task RunAsync_ReturnsRestAndLoadedStatisticsForTheGivenTarget()
    {
        var probe = new FakeLatencyProbeService { RoundTripMilliseconds = 15 };
        var useCase = new BufferbloatUseCase(probe, new FakeLoadGenerationService());

        var result = await useCase.RunAsync(FastOptions(), CancellationToken.None);

        Assert.Equal("1.1.1.1", result.AtRest.Target);
        Assert.Equal("1.1.1.1", result.UnderLoad.Target);
        Assert.Equal(TimeSpan.FromMilliseconds(15), result.AtRest.Median);
    }

    [Fact]
    public async Task RunAsync_PassesDirectionAndDurationThroughToTheLoadGenerator()
    {
        var loadGenerator = new FakeLoadGenerationService();
        var useCase = new BufferbloatUseCase(new FakeLatencyProbeService(), loadGenerator);
        var options = FastOptions(LoadDirection.Upload);

        await useCase.RunAsync(options, CancellationToken.None);

        Assert.Equal(LoadDirection.Upload, loadGenerator.LastDirection);
        Assert.Equal(options.LoadDuration, loadGenerator.LastDuration);
    }

    [Fact]
    public async Task RunAsync_SamplesLatencyWhileTheLoadIsRunning_NotJustBeforeAndAfter()
    {
        var probe = new FakeLatencyProbeService();
        var useCase = new BufferbloatUseCase(probe, new FakeLoadGenerationService());

        await useCase.RunAsync(FastOptions(), CancellationToken.None);

        // RestSampleCount (2) plus at least one sample taken during the load window.
        Assert.True(probe.CallCount > 2, $"Expected more than the 2 rest-period probes, got {probe.CallCount}.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(31)]
    public void RunAsync_DurationOutsideTheAllowedRange_ThrowsWithoutStartingAnything(int seconds)
    {
        var probe = new FakeLatencyProbeService();
        var loadGenerator = new FakeLoadGenerationService();
        var useCase = new BufferbloatUseCase(probe, loadGenerator);
        var options = new BufferbloatOptions("1.1.1.1", LoadDirection.Download, TimeSpan.FromSeconds(seconds));

        var ex = Assert.ThrowsAsync<ArgumentException>(() => useCase.RunAsync(options, CancellationToken.None));

        Assert.NotNull(ex);
        Assert.Equal(0, probe.CallCount);
        Assert.Null(loadGenerator.LastDuration);
    }

    [Fact]
    public async Task RunAsync_CallerCancelsBeforeStarting_ThrowsOperationCanceled()
    {
        var useCase = new BufferbloatUseCase(new FakeLatencyProbeService(), new FakeLoadGenerationService());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => useCase.RunAsync(FastOptions(), cts.Token));
    }
}
