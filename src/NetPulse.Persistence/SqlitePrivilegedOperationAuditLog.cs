using Microsoft.Data.Sqlite;
using NetPulse.Application.Ports;

namespace NetPulse.Persistence;

/// <summary>
/// Appends every privileged-operation attempt (Check/Capture/Apply/Verify/Rollback,
/// success or failure) to a local SQLite table. This log is never transmitted anywhere and
/// exists purely so the user (or the user inspecting their own machine) can see exactly
/// what NetPulse.PrivilegedAgent attempted and when.
/// </summary>
public sealed class SqlitePrivilegedOperationAuditLog : IPrivilegedOperationAuditLog
{
    private readonly string _connectionString;
    private static readonly SemaphoreSlim SchemaLock = new(1, 1);
    private bool _schemaReady;

    public SqlitePrivilegedOperationAuditLog(string databasePath)
    {
        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();
    }

    public async Task RecordAsync(PrivilegedOperationAuditEntry entry, CancellationToken cancellationToken)
    {
        await EnsureSchemaAsync(cancellationToken);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO PrivilegedOperationAudit (Timestamp, Capability, AdapterId, Phase, Succeeded, Details)
            VALUES ($timestamp, $capability, $adapterId, $phase, $succeeded, $details);
            """;
        command.Parameters.AddWithValue("$timestamp", entry.Timestamp.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$capability", entry.Capability);
        command.Parameters.AddWithValue("$adapterId", entry.AdapterId);
        command.Parameters.AddWithValue("$phase", entry.Phase);
        command.Parameters.AddWithValue("$succeeded", entry.Succeeded ? 1 : 0);
        command.Parameters.AddWithValue("$details", entry.Details);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PrivilegedOperationAuditEntry>> GetRecentAsync(int count, CancellationToken cancellationToken)
    {
        await EnsureSchemaAsync(cancellationToken);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Timestamp, Capability, AdapterId, Phase, Succeeded, Details
            FROM PrivilegedOperationAudit
            ORDER BY Timestamp DESC
            LIMIT $count;
            """;
        command.Parameters.AddWithValue("$count", count);

        var results = new List<PrivilegedOperationAuditEntry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new PrivilegedOperationAuditEntry(
                Timestamp: DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(0)),
                Capability: reader.GetString(1),
                AdapterId: reader.GetString(2),
                Phase: reader.GetString(3),
                Succeeded: reader.GetInt32(4) == 1,
                Details: reader.GetString(5)));
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
                CREATE TABLE IF NOT EXISTS PrivilegedOperationAudit (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Timestamp INTEGER NOT NULL,
                    Capability TEXT NOT NULL,
                    AdapterId TEXT NOT NULL,
                    Phase TEXT NOT NULL,
                    Succeeded INTEGER NOT NULL,
                    Details TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS IX_PrivilegedOperationAudit_Timestamp ON PrivilegedOperationAudit(Timestamp);
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
