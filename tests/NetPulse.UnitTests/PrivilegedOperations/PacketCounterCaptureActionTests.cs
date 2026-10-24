using NetPulse.Application.Ports;
using NetPulse.Domain.Optimization;
using NetPulse.Domain.PrivilegedOperations;
using NetPulse.PrivilegedAgent;

namespace NetPulse.UnitTests.PrivilegedOperations;

/// <summary>
/// Exercises PacketCounterCaptureAction entirely against fakes - no real elevated process,
/// no real pktmon invocation. See the class's own remarks for why that boundary is never
/// crossed by this automated session.
/// </summary>
public class PacketCounterCaptureActionTests
{
    private sealed class FakeElevatedLauncher : IElevatedOperationLauncher
    {
        public bool ShouldSucceed { get; set; } = true;
        public string? OutputToReturn { get; set; } = "Counters: Nic1 Packets=100 Drops=0";
        public PrivilegedOperationRequest? LastRequest { get; private set; }

        public Task<PrivilegedOperationResult> ExecuteAsync(PrivilegedOperationRequest request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(ShouldSucceed
                ? new PrivilegedOperationResult(request.Capability, true, OutputToReturn, null, DateTimeOffset.UtcNow)
                : new PrivilegedOperationResult(request.Capability, false, null, "elevation declined", DateTimeOffset.UtcNow));
        }
    }

    private sealed class FakeAuditLog : IPrivilegedOperationAuditLog
    {
        public List<PrivilegedOperationAuditEntry> Entries { get; } = new();

        public Task RecordAsync(PrivilegedOperationAuditEntry entry, CancellationToken cancellationToken)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task CaptureAsync_BuildsARequestForTheCapturePacketCountersCapability_WithDurationAsItsOnlyParameter()
    {
        var launcher = new FakeElevatedLauncher();
        var action = new PacketCounterCaptureAction(launcher, new FakeAuditLog());

        await action.CaptureAsync(15, CancellationToken.None);

        Assert.NotNull(launcher.LastRequest);
        Assert.Equal(PrivilegedCapability.CapturePacketCounters, launcher.LastRequest!.Capability);
        Assert.Equal(new[] { "15" }, launcher.LastRequest.Parameters);
    }

    [Fact]
    public async Task CaptureAsync_LauncherSucceeds_ReturnsTheRawOutputUnparsed()
    {
        var launcher = new FakeElevatedLauncher { OutputToReturn = "some raw pktmon text\nwith multiple lines" };
        var action = new PacketCounterCaptureAction(launcher, new FakeAuditLog());

        var result = await action.CaptureAsync(10, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(launcher.OutputToReturn, result.RawOutput);
    }

    [Fact]
    public async Task CaptureAsync_LauncherFails_ReturnsFailedWithError()
    {
        var launcher = new FakeElevatedLauncher { ShouldSucceed = false };
        var action = new PacketCounterCaptureAction(launcher, new FakeAuditLog());

        var result = await action.CaptureAsync(10, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("elevation declined", result.Error);
    }

    [Fact]
    public async Task CaptureAsync_AlwaysLogsExactlyOneCaptureAuditEntry()
    {
        var auditLog = new FakeAuditLog();
        var action = new PacketCounterCaptureAction(new FakeElevatedLauncher(), auditLog);

        await action.CaptureAsync(10, CancellationToken.None);

        Assert.Single(auditLog.Entries);
        Assert.Equal("Capture", auditLog.Entries[0].Phase);
        Assert.True(auditLog.Entries[0].Succeeded);
    }

    [Fact]
    public void Describe_NeverClaimsAWritePathOrPacketContentCapture()
    {
        var action = new PacketCounterCaptureAction(new FakeElevatedLauncher(), new FakeAuditLog());

        var descriptor = action.Describe();

        Assert.NotEmpty(descriptor.RequiredPermissions);
        Assert.Contains(descriptor.RequiredPermissions, p => p.Contains("UAC", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(descriptor.KnownLimitations, l => l.Contains("contenido", StringComparison.OrdinalIgnoreCase) || l.Contains("carga útil", StringComparison.OrdinalIgnoreCase));
    }
}
