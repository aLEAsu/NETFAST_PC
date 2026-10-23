using NetPulse.Application.Ports;
using NetPulse.Domain.Network;
using NetPulse.Domain.Optimization;
using NetPulse.Domain.PrivilegedOperations;
using NetPulse.PrivilegedAgent;

namespace NetPulse.UnitTests.PrivilegedOperations;

/// <summary>
/// Exercises SetAdapterDnsServersAction entirely against fakes - no real adapter, no real
/// elevated process, no real write. The one thing this suite can never test is whether the
/// real Windows write succeeds; that is deliberate (see ElevatedHelperOperationLauncher's
/// remarks) and is why NetPulse.SafetyTests exists as a separate, still-empty project for
/// whoever runs that verification manually.
/// </summary>
public class SetAdapterDnsServersActionTests
{
    private const string ValidAdapterId = "{20838DDC-A704-11F0-9799-806E6F6E6963}";

    private sealed class FakeAdapterInventory : INetworkAdapterInventoryService
    {
        public List<NetworkAdapterInfo> Adapters { get; } = new();

        public Task<IReadOnlyList<NetworkAdapterInfo>> GetAdaptersAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<NetworkAdapterInfo>>(Adapters);
    }

    private sealed class FakeDnsReader : IAdapterDnsConfigurationReader
    {
        public IReadOnlyList<string> ServersToReturn { get; set; } = new[] { "203.0.113.53" };
        public int CallCount { get; private set; }

        public Task<IReadOnlyList<string>> GetDnsServersAsync(string adapterId, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(ServersToReturn);
        }
    }

    private sealed class FakeElevatedLauncher : IElevatedOperationLauncher
    {
        public bool ShouldSucceed { get; set; } = true;
        public PrivilegedOperationRequest? LastRequest { get; private set; }

        public Task<PrivilegedOperationResult> ExecuteAsync(PrivilegedOperationRequest request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(ShouldSucceed
                ? new PrivilegedOperationResult(request.Capability, true, "ok", null, DateTimeOffset.UtcNow)
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

    private static NetworkAdapterInfo Adapter(string id) => new(
        id, "Ethernet", "Test Adapter", AdapterKind.Ethernet, IsUp: true,
        NegotiatedSpeedBitsPerSecond: 1_000_000_000,
        IPv4Addresses: new[] { "192.168.1.50" }, IPv6Addresses: Array.Empty<string>(),
        DnsServers: Array.Empty<string>(), Gateway: "192.168.1.1", Mtu: 1500, BytesSent: 0, BytesReceived: 0);

    [Fact]
    public async Task CheckAsync_AdapterExistsAndInputsAreValid_ReturnsTrue()
    {
        var inventory = new FakeAdapterInventory();
        inventory.Adapters.Add(Adapter(ValidAdapterId));
        var action = new SetAdapterDnsServersAction(ValidAdapterId, new[] { "1.1.1.1" }, inventory, new FakeDnsReader(), new FakeElevatedLauncher(), new FakeAuditLog());

        Assert.True(await action.CheckAsync(CancellationToken.None));
    }

    [Fact]
    public async Task CheckAsync_AdapterDoesNotExist_ReturnsFalse()
    {
        var inventory = new FakeAdapterInventory(); // empty
        var action = new SetAdapterDnsServersAction(ValidAdapterId, new[] { "1.1.1.1" }, inventory, new FakeDnsReader(), new FakeElevatedLauncher(), new FakeAuditLog());

        Assert.False(await action.CheckAsync(CancellationToken.None));
    }

    [Fact]
    public async Task CheckAsync_InvalidDnsServers_ReturnsFalseWithoutConsultingInventory()
    {
        var inventory = new FakeAdapterInventory();
        inventory.Adapters.Add(Adapter(ValidAdapterId));
        var action = new SetAdapterDnsServersAction(ValidAdapterId, new[] { "not-an-ip" }, inventory, new FakeDnsReader(), new FakeElevatedLauncher(), new FakeAuditLog());

        Assert.False(await action.CheckAsync(CancellationToken.None));
    }

    [Fact]
    public async Task CaptureAsync_ReturnsCurrentServersJoinedAndLogsTheCapture()
    {
        var reader = new FakeDnsReader { ServersToReturn = new[] { "9.9.9.9", "149.112.112.112" } };
        var auditLog = new FakeAuditLog();
        var action = new SetAdapterDnsServersAction(ValidAdapterId, new[] { "1.1.1.1" }, new FakeAdapterInventory(), reader, new FakeElevatedLauncher(), auditLog);

        var captured = await action.CaptureAsync(CancellationToken.None);

        Assert.Equal("9.9.9.9,149.112.112.112", captured);
        Assert.Single(auditLog.Entries);
        Assert.Equal("Capture", auditLog.Entries[0].Phase);
        Assert.True(auditLog.Entries[0].Succeeded);
    }

    [Fact]
    public async Task ApplyAsync_LauncherSucceeds_ReturnsAppliedNotYetVerifiedAndLogsIt()
    {
        var launcher = new FakeElevatedLauncher { ShouldSucceed = true };
        var auditLog = new FakeAuditLog();
        var action = new SetAdapterDnsServersAction(ValidAdapterId, new[] { "1.1.1.1", "8.8.8.8" }, new FakeAdapterInventory(), new FakeDnsReader(), launcher, auditLog);

        var result = await action.ApplyAsync(CancellationToken.None);

        Assert.Equal(OptimizationOutcome.AppliedNotYetVerified, result.Outcome);
        Assert.Null(result.Error);
        Assert.NotNull(launcher.LastRequest);
        Assert.Equal(PrivilegedCapability.SetAdapterDnsServers, launcher.LastRequest!.Capability);
        Assert.Equal(new[] { "1.1.1.1", "8.8.8.8" }, launcher.LastRequest.Parameters);
        Assert.Contains(auditLog.Entries, e => e.Phase == "Apply" && e.Succeeded);
    }

    [Fact]
    public async Task ApplyAsync_LauncherFails_ReturnsFailedWithErrorAndLogsIt()
    {
        var launcher = new FakeElevatedLauncher { ShouldSucceed = false };
        var auditLog = new FakeAuditLog();
        var action = new SetAdapterDnsServersAction(ValidAdapterId, new[] { "1.1.1.1" }, new FakeAdapterInventory(), new FakeDnsReader(), launcher, auditLog);

        var result = await action.ApplyAsync(CancellationToken.None);

        Assert.Equal(OptimizationOutcome.Failed, result.Outcome);
        Assert.Equal("elevation declined", result.Error);
        Assert.Contains(auditLog.Entries, e => e.Phase == "Apply" && !e.Succeeded);
    }

    [Fact]
    public async Task VerifyAsync_ObservedServersMatchRequested_ReturnsVerifiedImproved()
    {
        var reader = new FakeDnsReader { ServersToReturn = new[] { "1.1.1.1", "8.8.8.8" } };
        var action = new SetAdapterDnsServersAction(ValidAdapterId, new[] { "1.1.1.1", "8.8.8.8" }, new FakeAdapterInventory(), reader, new FakeElevatedLauncher(), new FakeAuditLog());

        var result = await action.VerifyAsync(CancellationToken.None);

        Assert.Equal(OptimizationOutcome.VerifiedImproved, result.Outcome);
    }

    [Fact]
    public async Task VerifyAsync_ObservedServersDoNotMatch_ReturnsVerifiedInconclusive()
    {
        var reader = new FakeDnsReader { ServersToReturn = new[] { "203.0.113.53" } }; // unchanged - apply did not take effect
        var action = new SetAdapterDnsServersAction(ValidAdapterId, new[] { "1.1.1.1" }, new FakeAdapterInventory(), reader, new FakeElevatedLauncher(), new FakeAuditLog());

        var result = await action.VerifyAsync(CancellationToken.None);

        Assert.Equal(OptimizationOutcome.VerifiedInconclusive, result.Outcome);
    }

    [Fact]
    public async Task RollbackAsync_RestoresTheExactCapturedServerList()
    {
        var launcher = new FakeElevatedLauncher { ShouldSucceed = true };
        var action = new SetAdapterDnsServersAction(ValidAdapterId, new[] { "1.1.1.1" }, new FakeAdapterInventory(), new FakeDnsReader(), launcher, new FakeAuditLog());

        var result = await action.RollbackAsync("9.9.9.9,149.112.112.112", CancellationToken.None);

        Assert.Equal(OptimizationOutcome.RolledBack, result.Outcome);
        Assert.Equal(new[] { "9.9.9.9", "149.112.112.112" }, launcher.LastRequest!.Parameters);
    }

    [Fact]
    public async Task RollbackAsync_LauncherFails_ReturnsFailed()
    {
        var launcher = new FakeElevatedLauncher { ShouldSucceed = false };
        var action = new SetAdapterDnsServersAction(ValidAdapterId, new[] { "1.1.1.1" }, new FakeAdapterInventory(), new FakeDnsReader(), launcher, new FakeAuditLog());

        var result = await action.RollbackAsync("9.9.9.9", CancellationToken.None);

        Assert.Equal(OptimizationOutcome.Failed, result.Outcome);
    }

    [Fact]
    public void Describe_NeverClaimsItCanWriteAnythingItself()
    {
        var action = new SetAdapterDnsServersAction(ValidAdapterId, new[] { "1.1.1.1" }, new FakeAdapterInventory(), new FakeDnsReader(), new FakeElevatedLauncher(), new FakeAuditLog());

        var descriptor = action.Describe();

        Assert.Equal(OptimizationRisk.Medium, descriptor.Risk);
        Assert.NotEmpty(descriptor.RequiredPermissions);
        Assert.Contains(descriptor.RequiredPermissions, p => p.Contains("UAC", StringComparison.OrdinalIgnoreCase));
    }
}
