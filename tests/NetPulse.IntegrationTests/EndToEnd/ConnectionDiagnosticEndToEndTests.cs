using NetPulse.Application.UseCases;
using NetPulse.Domain.Diagnostics;
using NetPulse.Infrastructure.Windows.Network;
using NetPulse.Persistence;

namespace NetPulse.IntegrationTests.EndToEnd;

/// <summary>
/// Runs the full diagnostic pipeline with no fakes anywhere: real ICMP probe, real SQLite
/// file. This is what actually exercises the wiring in App.xaml.cs without needing WPF.
/// </summary>
public class ConnectionDiagnosticEndToEndTests : IDisposable
{
    private readonly string _databasePath;
    private readonly ConnectionDiagnosticUseCase _useCase;

    public ConnectionDiagnosticEndToEndTests()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"netpulse-e2e-{Guid.NewGuid():N}.db");
        _useCase = new ConnectionDiagnosticUseCase(
            new IcmpLatencyProbeService(),
            new SqliteDiagnosticHistoryRepository(_databasePath));
    }

    [Fact]
    public async Task RunAsync_AgainstLoopback_ProducesAHealthyFindingAndPersistsIt()
    {
        var options = new DiagnosticOptions(
            Targets: new[] { "127.0.0.1" },
            SampleCount: 10,
            IntervalBetweenSamples: TimeSpan.FromMilliseconds(20));

        var results = await _useCase.RunAsync(options, CancellationToken.None);

        Assert.Single(results);
        var result = results[0];
        Assert.Equal(10, result.Statistics.SampleCount);
        Assert.Equal(0, result.Statistics.PacketLossPercent);
        Assert.Equal(DiagnosticConfidence.Healthy, result.Finding.Confidence);

        var history = await new SqliteDiagnosticHistoryRepository(_databasePath)
            .GetRecentAsync("127.0.0.1", 10, CancellationToken.None);
        Assert.Single(history);
    }

    [Fact]
    public async Task RunAsync_AgainstANonRoutableTarget_ProducesANoResponseFinding()
    {
        var options = new DiagnosticOptions(
            Targets: new[] { "203.0.113.1" },
            SampleCount: 3,
            IntervalBetweenSamples: TimeSpan.Zero,
            ProbeTimeout: TimeSpan.FromMilliseconds(500));

        var results = await _useCase.RunAsync(options, CancellationToken.None);

        Assert.Equal(100.0, results[0].Statistics.PacketLossPercent);
        Assert.Equal("connectivity.no-response", results[0].Finding.Id);
    }

    [Fact]
    public async Task RunAsync_MultipleTargets_ProducesOneIndependentFindingPerTarget()
    {
        var options = new DiagnosticOptions(
            Targets: new[] { "127.0.0.1", "203.0.113.1" },
            SampleCount: 3,
            IntervalBetweenSamples: TimeSpan.Zero,
            ProbeTimeout: TimeSpan.FromMilliseconds(500));

        var results = await _useCase.RunAsync(options, CancellationToken.None);

        Assert.Equal(2, results.Count);
        Assert.Equal(DiagnosticConfidence.Hypothesis, results[0].Finding.Confidence); // only 3 samples
        Assert.Equal("203.0.113.1", results[1].Statistics.Target);
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_databasePath))
                File.Delete(_databasePath);
        }
        catch (IOException)
        {
        }
    }
}
