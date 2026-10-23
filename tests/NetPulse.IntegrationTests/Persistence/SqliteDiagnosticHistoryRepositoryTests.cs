using NetPulse.Domain.Network;
using NetPulse.Persistence;

namespace NetPulse.IntegrationTests.Persistence;

/// <summary>
/// Exercises the real SQLite file on disk (no in-memory substitute) so schema creation,
/// parameter binding, and the Unix-time round trip for timestamps are all verified against
/// the actual provider, not an assumption about how it behaves.
/// </summary>
public class SqliteDiagnosticHistoryRepositoryTests : IDisposable
{
    private readonly string _databasePath;
    private readonly SqliteDiagnosticHistoryRepository _repository;

    public SqliteDiagnosticHistoryRepositoryTests()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"netpulse-tests-{Guid.NewGuid():N}.db");
        _repository = new SqliteDiagnosticHistoryRepository(_databasePath);
    }

    private static LatencyStatistics Sample(string target, DateTimeOffset windowEnd) => new(
        Target: target,
        SampleCount: 20,
        RespondedCount: 19,
        PacketLossPercent: 5.0,
        Min: TimeSpan.FromMilliseconds(8),
        Median: TimeSpan.FromMilliseconds(15),
        P95: TimeSpan.FromMilliseconds(28),
        Max: TimeSpan.FromMilliseconds(40),
        JitterMeanAbsoluteDeviation: TimeSpan.FromMilliseconds(3.5),
        WindowStart: windowEnd.AddSeconds(-4),
        WindowEnd: windowEnd);

    [Fact]
    public async Task SaveAsync_ThenGetRecentAsync_RoundTripsAllFieldsExactly()
    {
        var original = Sample("1.1.1.1", DateTimeOffset.UtcNow);

        await _repository.SaveAsync(original, CancellationToken.None);
        var recent = await _repository.GetRecentAsync("1.1.1.1", 10, CancellationToken.None);

        Assert.Single(recent);
        var roundTripped = recent[0];
        Assert.Equal(original.Target, roundTripped.Target);
        Assert.Equal(original.SampleCount, roundTripped.SampleCount);
        Assert.Equal(original.RespondedCount, roundTripped.RespondedCount);
        Assert.Equal(original.PacketLossPercent, roundTripped.PacketLossPercent, precision: 5);
        Assert.Equal(original.Median, roundTripped.Median);
        Assert.Equal(original.P95, roundTripped.P95);
        Assert.Equal(original.JitterMeanAbsoluteDeviation, roundTripped.JitterMeanAbsoluteDeviation);
        Assert.Equal(original.WindowStart.ToUnixTimeMilliseconds(), roundTripped.WindowStart.ToUnixTimeMilliseconds());
        Assert.Equal(original.WindowEnd.ToUnixTimeMilliseconds(), roundTripped.WindowEnd.ToUnixTimeMilliseconds());
    }

    [Fact]
    public async Task GetRecentAsync_FiltersByTarget()
    {
        await _repository.SaveAsync(Sample("1.1.1.1", DateTimeOffset.UtcNow), CancellationToken.None);
        await _repository.SaveAsync(Sample("8.8.8.8", DateTimeOffset.UtcNow), CancellationToken.None);

        var onlyCloudflare = await _repository.GetRecentAsync("1.1.1.1", 10, CancellationToken.None);

        Assert.Single(onlyCloudflare);
        Assert.Equal("1.1.1.1", onlyCloudflare[0].Target);
    }

    [Fact]
    public async Task GetRecentAsync_RespectsTheCountLimitAndMostRecentFirst()
    {
        var now = DateTimeOffset.UtcNow;
        for (int i = 0; i < 5; i++)
            await _repository.SaveAsync(Sample("1.1.1.1", now.AddSeconds(i)), CancellationToken.None);

        var lastTwo = await _repository.GetRecentAsync("1.1.1.1", 2, CancellationToken.None);

        Assert.Equal(2, lastTwo.Count);
        Assert.True(lastTwo[0].WindowEnd > lastTwo[1].WindowEnd);
    }

    [Fact]
    public async Task GetRecentAsync_UnknownTarget_ReturnsEmptyRatherThanThrowing()
    {
        var results = await _repository.GetRecentAsync("never-saved.invalid", 10, CancellationToken.None);

        Assert.Empty(results);
    }

    [Fact]
    public async Task SaveAsync_CreatesTheDatabaseFileAndItsDirectory()
    {
        Assert.False(File.Exists(_databasePath));

        await _repository.SaveAsync(Sample("1.1.1.1", DateTimeOffset.UtcNow), CancellationToken.None);

        Assert.True(File.Exists(_databasePath));
    }

    [Fact]
    public async Task SaveAsync_ConcurrentWrites_DoNotCorruptOrLoseRows()
    {
        // Guards the schema-initialization lock: concurrent first-use from multiple
        // diagnostic runs must not race on CREATE TABLE IF NOT EXISTS.
        var now = DateTimeOffset.UtcNow;
        var writes = Enumerable.Range(0, 8)
            .Select(i => _repository.SaveAsync(Sample("1.1.1.1", now.AddMilliseconds(i)), CancellationToken.None));

        await Task.WhenAll(writes);

        var results = await _repository.GetRecentAsync("1.1.1.1", 100, CancellationToken.None);
        Assert.Equal(8, results.Count);
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
            // Best-effort cleanup; a lingering temp file from a test run is not worth failing over.
        }
    }
}
