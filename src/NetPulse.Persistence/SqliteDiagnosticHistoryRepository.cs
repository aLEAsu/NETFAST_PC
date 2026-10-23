using Microsoft.Data.Sqlite;
using NetPulse.Application.Ports;
using NetPulse.Domain.Network;

namespace NetPulse.Persistence;

/// <summary>
/// Local-only history store. Holds aggregated statistics per run, never raw packet payloads,
/// per the project's privacy requirement that captured data stay minimal and local.
/// </summary>
public sealed class SqliteDiagnosticHistoryRepository : IDiagnosticHistoryRepository
{
    private readonly string _connectionString;
    private static readonly SemaphoreSlim SchemaLock = new(1, 1);
    private bool _schemaReady;

    public SqliteDiagnosticHistoryRepository(string databasePath)
    {
        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();
    }

    public async Task SaveAsync(LatencyStatistics statistics, CancellationToken cancellationToken)
    {
        await EnsureSchemaAsync(cancellationToken);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO DiagnosticRuns
                (Target, SampleCount, RespondedCount, PacketLossPercent, MinMs, MedianMs, P95Ms, MaxMs, JitterMs, WindowStart, WindowEnd)
            VALUES
                ($target, $sampleCount, $respondedCount, $lossPercent, $minMs, $medianMs, $p95Ms, $maxMs, $jitterMs, $windowStart, $windowEnd);
            """;

        command.Parameters.AddWithValue("$target", statistics.Target);
        command.Parameters.AddWithValue("$sampleCount", statistics.SampleCount);
        command.Parameters.AddWithValue("$respondedCount", statistics.RespondedCount);
        command.Parameters.AddWithValue("$lossPercent", statistics.PacketLossPercent);
        command.Parameters.AddWithValue("$minMs", statistics.Min.TotalMilliseconds);
        command.Parameters.AddWithValue("$medianMs", statistics.Median.TotalMilliseconds);
        command.Parameters.AddWithValue("$p95Ms", statistics.P95.TotalMilliseconds);
        command.Parameters.AddWithValue("$maxMs", statistics.Max.TotalMilliseconds);
        command.Parameters.AddWithValue("$jitterMs", statistics.JitterMeanAbsoluteDeviation.TotalMilliseconds);
        command.Parameters.AddWithValue("$windowStart", statistics.WindowStart.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$windowEnd", statistics.WindowEnd.ToUnixTimeMilliseconds());

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<LatencyStatistics>> GetRecentAsync(string target, int count, CancellationToken cancellationToken)
    {
        await EnsureSchemaAsync(cancellationToken);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Target, SampleCount, RespondedCount, PacketLossPercent, MinMs, MedianMs, P95Ms, MaxMs, JitterMs, WindowStart, WindowEnd
            FROM DiagnosticRuns
            WHERE Target = $target
            ORDER BY WindowEnd DESC
            LIMIT $count;
            """;
        command.Parameters.AddWithValue("$target", target);
        command.Parameters.AddWithValue("$count", count);

        var results = new List<LatencyStatistics>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new LatencyStatistics(
                Target: reader.GetString(0),
                SampleCount: reader.GetInt32(1),
                RespondedCount: reader.GetInt32(2),
                PacketLossPercent: reader.GetDouble(3),
                Min: TimeSpan.FromMilliseconds(reader.GetDouble(4)),
                Median: TimeSpan.FromMilliseconds(reader.GetDouble(5)),
                P95: TimeSpan.FromMilliseconds(reader.GetDouble(6)),
                Max: TimeSpan.FromMilliseconds(reader.GetDouble(7)),
                JitterMeanAbsoluteDeviation: TimeSpan.FromMilliseconds(reader.GetDouble(8)),
                WindowStart: DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(9)),
                WindowEnd: DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(10))));
        }

        return results;
    }

    private async Task EnsureSchemaAsync(CancellationToken cancellationToken)
    {
        if (_schemaReady)
            return;

        await SchemaLock.WaitAsync(cancellationToken);
        try
        {
            if (_schemaReady)
                return;

            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS DiagnosticRuns (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Target TEXT NOT NULL,
                    SampleCount INTEGER NOT NULL,
                    RespondedCount INTEGER NOT NULL,
                    PacketLossPercent REAL NOT NULL,
                    MinMs REAL NOT NULL,
                    MedianMs REAL NOT NULL,
                    P95Ms REAL NOT NULL,
                    MaxMs REAL NOT NULL,
                    JitterMs REAL NOT NULL,
                    WindowStart INTEGER NOT NULL,
                    WindowEnd INTEGER NOT NULL
                );
                CREATE INDEX IF NOT EXISTS IX_DiagnosticRuns_Target_WindowEnd ON DiagnosticRuns(Target, WindowEnd);
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);

            _schemaReady = true;
        }
        finally
        {
            SchemaLock.Release();
        }
    }
}
