using NetPulse.Application.Ports;
using NetPulse.Domain.Network;
using NetPulse.Domain.Optimization;
using NetPulse.Domain.PrivilegedOperations;
using NetPulse.PrivilegedAgent;

namespace NetPulse.SafetyTests;

/// <summary>
/// Tests the SAFETY GUARANTEES of the full Check/Capture/Apply/Verify/Rollback lifecycle,
/// not individual methods in isolation (that's NetPulse.UnitTests). The brief's principle
/// this suite exists to hold the code to: never report a change as successful just because
/// a command returned without error, and always be able to recover to the state captured
/// before the change - even when the write path reports failure. All fakes - no real
/// elevation, no real write, per the project's documented policy on that boundary.
/// </summary>
public class RecoveryLifecycleTests
{
    private const string ValidAdapterId = "{20838DDC-A704-11F0-9799-806E6F6E6963}";

    private sealed class FakeAdapterInventory : INetworkAdapterInventoryService
    {
        public Task<IReadOnlyList<NetworkAdapterInfo>> GetAdaptersAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<NetworkAdapterInfo>>(new[]
            {
                new NetworkAdapterInfo(ValidAdapterId, "Ethernet", "Test Adapter", AdapterKind.Ethernet, true,
                    1_000_000_000, new[] { "192.168.1.50" }, Array.Empty<string>(), Array.Empty<string>(),
                    "192.168.1.1", 1500, 0, 0),
            });
    }

    private sealed class FakeDnsReader : IAdapterDnsConfigurationReader
    {
        public IReadOnlyList<string> ServersToReturn { get; set; } = Array.Empty<string>();

        public Task<IReadOnlyList<string>> GetDnsServersAsync(string adapterId, CancellationToken cancellationToken) =>
            Task.FromResult(ServersToReturn);
    }

    private sealed class FakeElevatedLauncher : IElevatedOperationLauncher
    {
        public bool ShouldSucceed { get; set; } = true;
        public List<PrivilegedOperationRequest> Requests { get; } = new();

        public Task<PrivilegedOperationResult> ExecuteAsync(PrivilegedOperationRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(ShouldSucceed
                ? new PrivilegedOperationResult(request.Capability, true, "ok", null, DateTimeOffset.UtcNow)
                : new PrivilegedOperationResult(request.Capability, false, null, "simulated failure", DateTimeOffset.UtcNow));
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
    public async Task ApplyAsync_NeverReportsVerifiedSuccess_OnlyAppliedNotYetVerified()
    {
        // The brief's "never claim success just because the command returned without
        // error" principle, as a hard assertion on the type system: Apply's own outcome
        // must never be one of the Verified* values - only a separate VerifyAsync call can
        // produce those.
        var launcher = new FakeElevatedLauncher { ShouldSucceed = true };
        var action = new SetAdapterDnsServersAction(ValidAdapterId, new[] { "1.1.1.1" },
            new FakeAdapterInventory(), new FakeDnsReader(), launcher, new FakeAuditLog());

        var applyResult = await action.ApplyAsync(CancellationToken.None);

        Assert.Equal(OptimizationOutcome.AppliedNotYetVerified, applyResult.Outcome);
        Assert.NotEqual(OptimizationOutcome.VerifiedImproved, applyResult.Outcome);
    }

    [Fact]
    public async Task FullLifecycle_CaptureThenApplyThenVerifyMismatchThenRollback_RestoresExactCapturedState()
    {
        // The scenario the brief cares about most: the write silently didn't take effect
        // (Verify disagrees with what was requested), and the system must still be able to
        // get back to exactly what Capture saw before anything happened - not an
        // approximation, not "probably fine."
        var dnsReader = new FakeDnsReader { ServersToReturn = new[] { "203.0.113.53" } }; // original state
        var launcher = new FakeElevatedLauncher { ShouldSucceed = true };
        var auditLog = new FakeAuditLog();
        var action = new SetAdapterDnsServersAction(ValidAdapterId, new[] { "1.1.1.1" },
            new FakeAdapterInventory(), dnsReader, launcher, auditLog);

        Assert.True(await action.CheckAsync(CancellationToken.None));
        var capturedState = await action.CaptureAsync(CancellationToken.None);
        Assert.Equal("203.0.113.53", capturedState);

        var applyResult = await action.ApplyAsync(CancellationToken.None);
        Assert.Equal(OptimizationOutcome.AppliedNotYetVerified, applyResult.Outcome);

        // Reader still reports the original value - the write did not actually take effect,
        // even though the elevated helper reported success (e.g. a driver that silently
        // ignores the change).
        var verifyResult = await action.VerifyAsync(CancellationToken.None);
        Assert.Equal(OptimizationOutcome.VerifiedInconclusive, verifyResult.Outcome);

        var rollbackResult = await action.RollbackAsync(capturedState, CancellationToken.None);

        Assert.Equal(OptimizationOutcome.RolledBack, rollbackResult.Outcome);
        var rollbackRequest = launcher.Requests.Last();
        Assert.Equal(new[] { "203.0.113.53" }, rollbackRequest.Parameters);

        // The full lifecycle must be fully audited: Capture, Apply, Verify, Rollback.
        Assert.Equal(new[] { "Capture", "Apply", "Verify", "Rollback" }, auditLog.Entries.Select(e => e.Phase).ToArray());
    }

    [Fact]
    public async Task FullLifecycle_AdapterHadNoStaticDns_CaptureApplyRollback_RestoresAutomaticDns()
    {
        // The specific regression this suite exists to guard: the most common starting
        // configuration (DHCP-assigned DNS, no static override) must be fully recoverable,
        // not just representable.
        var dnsReader = new FakeDnsReader { ServersToReturn = Array.Empty<string>() };
        var launcher = new FakeElevatedLauncher { ShouldSucceed = true };
        var action = new SetAdapterDnsServersAction(ValidAdapterId, new[] { "1.1.1.1" },
            new FakeAdapterInventory(), dnsReader, launcher, new FakeAuditLog());

        var capturedState = await action.CaptureAsync(CancellationToken.None);
        Assert.Equal(string.Empty, capturedState);

        await action.ApplyAsync(CancellationToken.None);
        var rollbackResult = await action.RollbackAsync(capturedState, CancellationToken.None);

        Assert.Equal(OptimizationOutcome.RolledBack, rollbackResult.Outcome);
        Assert.Empty(launcher.Requests.Last().Parameters);
    }

    [Fact]
    public async Task ApplyAsync_ElevationDeclined_ReportsFailedAndStillAllowsRollbackAttempt()
    {
        // If the user declines the UAC prompt, the action must report that cleanly (never
        // crash, never claim success) and the captured state must still be usable - nothing
        // about a failed Apply should corrupt or discard the rollback path.
        var dnsReader = new FakeDnsReader { ServersToReturn = new[] { "9.9.9.9" } };
        var launcher = new FakeElevatedLauncher { ShouldSucceed = false };
        var auditLog = new FakeAuditLog();
        var action = new SetAdapterDnsServersAction(ValidAdapterId, new[] { "1.1.1.1" },
            new FakeAdapterInventory(), dnsReader, launcher, auditLog);

        var capturedState = await action.CaptureAsync(CancellationToken.None);
        var applyResult = await action.ApplyAsync(CancellationToken.None);

        Assert.Equal(OptimizationOutcome.Failed, applyResult.Outcome);
        Assert.NotNull(applyResult.Error);

        var rollbackResult = await action.RollbackAsync(capturedState, CancellationToken.None);
        Assert.Equal(OptimizationOutcome.Failed, rollbackResult.Outcome); // launcher still declines, consistently
        Assert.Contains(auditLog.Entries, e => e.Phase == "Apply" && !e.Succeeded);
        Assert.Contains(auditLog.Entries, e => e.Phase == "Rollback" && !e.Succeeded);
    }

    [Fact]
    public void Descriptor_AlwaysDeclaresAConcreteRollbackMethod()
    {
        // Every action in this codebase must be reversible by declaration, not just by
        // accident of implementation - a blank or missing RollbackMethod is itself a defect.
        var action = new SetAdapterDnsServersAction(ValidAdapterId, new[] { "1.1.1.1" },
            new FakeAdapterInventory(), new FakeDnsReader(), new FakeElevatedLauncher(), new FakeAuditLog());

        var descriptor = action.Describe();

        Assert.False(string.IsNullOrWhiteSpace(descriptor.RollbackMethod));
        Assert.NotEqual("Not applicable", descriptor.RollbackMethod);
    }
}
