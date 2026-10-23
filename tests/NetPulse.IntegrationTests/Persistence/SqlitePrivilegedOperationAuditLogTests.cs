using NetPulse.Application.Ports;
using NetPulse.Persistence;

namespace NetPulse.IntegrationTests.Persistence;

/// <summary>Real SQLite file I/O, no fakes - mirrors SqliteDiagnosticHistoryRepositoryTests.</summary>
public class SqlitePrivilegedOperationAuditLogTests : IDisposable
{
    private readonly string _databasePath;
    private readonly SqlitePrivilegedOperationAuditLog _auditLog;

    public SqlitePrivilegedOperationAuditLogTests()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"netpulse-audit-tests-{Guid.NewGuid():N}.db");
        _auditLog = new SqlitePrivilegedOperationAuditLog(_databasePath);
    }

    [Fact]
    public async Task RecordAsync_ThenGetRecentAsync_RoundTripsTheEntry()
    {
        var entry = new PrivilegedOperationAuditEntry(
            DateTimeOffset.UtcNow, "SetAdapterDnsServers", "{20838DDC-A704-11F0-9799-806E6F6E6963}",
            "Apply", true, "DNS servers set to 1.1.1.1,8.8.8.8.");

        await _auditLog.RecordAsync(entry, CancellationToken.None);
        var recent = await _auditLog.GetRecentAsync(10, CancellationToken.None);

        Assert.Single(recent);
        Assert.Equal(entry.Capability, recent[0].Capability);
        Assert.Equal(entry.AdapterId, recent[0].AdapterId);
        Assert.Equal(entry.Phase, recent[0].Phase);
        Assert.Equal(entry.Succeeded, recent[0].Succeeded);
        Assert.Equal(entry.Details, recent[0].Details);
    }

    [Fact]
    public async Task RecordAsync_MultipleEntries_GetRecentAsync_ReturnsMostRecentFirst()
    {
        var now = DateTimeOffset.UtcNow;
        for (int i = 0; i < 5; i++)
        {
            await _auditLog.RecordAsync(
                new PrivilegedOperationAuditEntry(now.AddSeconds(i), "SetAdapterDnsServers", "adapter-1", "Check", true, $"attempt {i}"),
                CancellationToken.None);
        }

        var recent = await _auditLog.GetRecentAsync(2, CancellationToken.None);

        Assert.Equal(2, recent.Count);
        Assert.Equal("attempt 4", recent[0].Details);
        Assert.Equal("attempt 3", recent[1].Details);
    }

    [Fact]
    public async Task RecordAsync_FailedAttempt_PreservesSucceededFalse()
    {
        await _auditLog.RecordAsync(
            new PrivilegedOperationAuditEntry(DateTimeOffset.UtcNow, "SetAdapterDnsServers", "adapter-1", "Apply", false, "elevation declined"),
            CancellationToken.None);

        var recent = await _auditLog.GetRecentAsync(10, CancellationToken.None);

        Assert.False(recent[0].Succeeded);
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
