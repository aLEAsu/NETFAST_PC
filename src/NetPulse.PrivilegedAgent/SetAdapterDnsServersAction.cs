using NetPulse.Application.Ports;
using NetPulse.Domain.Optimization;
using NetPulse.Domain.PrivilegedOperations;

namespace NetPulse.PrivilegedAgent;

/// <summary>
/// The first real <see cref="IOptimizationAction"/> in this codebase: overwrites the DNS
/// server search order on one adapter, with capture/verify/rollback built in. Every write
/// (<see cref="ApplyAsync"/>, <see cref="RollbackAsync"/>) goes through
/// <see cref="IElevatedOperationLauncher"/>, which launches a separate, short-lived elevated
/// process for exactly that one operation - this class itself has no ability to write
/// anything on its own.
/// </summary>
public sealed class SetAdapterDnsServersAction : IOptimizationAction
{
    private readonly string _adapterId;
    private readonly IReadOnlyList<string> _requestedDnsServers;
    private readonly INetworkAdapterInventoryService _adapterInventory;
    private readonly IAdapterDnsConfigurationReader _dnsReader;
    private readonly IElevatedOperationLauncher _elevatedLauncher;
    private readonly IPrivilegedOperationAuditLog _auditLog;

    public SetAdapterDnsServersAction(
        string adapterId,
        IReadOnlyList<string> requestedDnsServers,
        INetworkAdapterInventoryService adapterInventory,
        IAdapterDnsConfigurationReader dnsReader,
        IElevatedOperationLauncher elevatedLauncher,
        IPrivilegedOperationAuditLog auditLog)
    {
        _adapterId = adapterId;
        _requestedDnsServers = requestedDnsServers;
        _adapterInventory = adapterInventory;
        _dnsReader = dnsReader;
        _elevatedLauncher = elevatedLauncher;
        _auditLog = auditLog;
    }

    public OptimizationDescriptor Describe() => new(
        Id: "action.set-adapter-dns-servers",
        Title: "Set DNS servers on this adapter",
        Objective: "Replace the adapter's configured DNS servers with the requested, validated set.",
        Evidence: "User-selected recommendation; this action does not decide on its own that DNS is the problem.",
        Preconditions: new[] { "The adapter exists.", "The requested server list passes DnsServerAddressValidator." },
        Risk: OptimizationRisk.Medium,
        RequiredPermissions: new[] { "Administrator elevation for this one operation (UAC prompt on the elevated helper process)." },
        VerificationMethod: "Re-read the adapter's DNS configuration after applying and confirm it matches the requested servers.",
        SuccessCriterion: "The adapter's DNS server list exactly matches the requested list after applying.",
        FailureCriterion: "The elevated helper reports failure, or the post-apply read does not match the requested list.",
        RollbackMethod: "Re-apply the DNS server list captured before this action ran, through the same elevated helper.",
        ReferenceDocumentation: new[] { "Win32_NetworkAdapterConfiguration.SetDNSServerSearchOrder (Windows WMI)." },
        KnownLimitations: new[]
        {
            "Changing DNS does not guarantee lower latency or fewer errors; it only changes which resolver is used.",
            "Some networks override DNS at the router/DHCP level regardless of this setting.",
            "Requires the user to approve a UAC prompt for the elevated helper; this action cannot and does not bypass that.",
        });

    public async Task<bool> CheckAsync(CancellationToken cancellationToken)
    {
        if (!AdapterIdValidator.TryValidate(_adapterId, out _))
            return false;

        if (!DnsServerAddressValidator.TryValidate(_requestedDnsServers, out _))
            return false;

        var adapters = await _adapterInventory.GetAdaptersAsync(cancellationToken);
        var normalizedTarget = AdapterIdValidator.Normalize(_adapterId);
        return adapters.Any(a => AdapterIdValidator.Normalize(a.Id) == normalizedTarget);
    }

    public async Task<string> CaptureAsync(CancellationToken cancellationToken)
    {
        var currentServers = await _dnsReader.GetDnsServersAsync(_adapterId, cancellationToken);
        var captured = string.Join(",", currentServers);

        await _auditLog.RecordAsync(
            new PrivilegedOperationAuditEntry(DateTimeOffset.UtcNow, nameof(PrivilegedCapability.SetAdapterDnsServers), _adapterId, "Capture", true, $"Captured: {captured}"),
            cancellationToken);

        return captured;
    }

    public async Task<OptimizationResult> ApplyAsync(CancellationToken cancellationToken)
    {
        var request = new PrivilegedOperationRequest(
            PrivilegedCapability.SetAdapterDnsServers,
            _adapterId,
            _requestedDnsServers,
            DateTimeOffset.UtcNow);

        var result = await _elevatedLauncher.ExecuteAsync(request, cancellationToken);

        await _auditLog.RecordAsync(
            new PrivilegedOperationAuditEntry(DateTimeOffset.UtcNow, nameof(PrivilegedCapability.SetAdapterDnsServers), _adapterId, "Apply", result.Succeeded, result.Output ?? result.Error ?? string.Empty),
            cancellationToken);

        return new OptimizationResult(
            Describe().Id,
            result.Succeeded ? OptimizationOutcome.AppliedNotYetVerified : OptimizationOutcome.Failed,
            DateTimeOffset.UtcNow,
            result.Output,
            result.Succeeded ? null : result.Error);
    }

    public async Task<OptimizationResult> VerifyAsync(CancellationToken cancellationToken)
    {
        var currentServers = await _dnsReader.GetDnsServersAsync(_adapterId, cancellationToken);
        bool matches = currentServers.SequenceEqual(_requestedDnsServers);

        await _auditLog.RecordAsync(
            new PrivilegedOperationAuditEntry(DateTimeOffset.UtcNow, nameof(PrivilegedCapability.SetAdapterDnsServers), _adapterId, "Verify", matches, $"Observed: {string.Join(",", currentServers)}"),
            cancellationToken);

        return new OptimizationResult(
            Describe().Id,
            matches ? OptimizationOutcome.VerifiedImproved : OptimizationOutcome.VerifiedInconclusive,
            DateTimeOffset.UtcNow,
            $"Observed DNS servers: {string.Join(",", currentServers)}",
            null);
    }

    public async Task<OptimizationResult> RollbackAsync(string capturedState, CancellationToken cancellationToken)
    {
        var originalServers = capturedState.Split(',', StringSplitOptions.RemoveEmptyEntries);

        var request = new PrivilegedOperationRequest(
            PrivilegedCapability.SetAdapterDnsServers,
            _adapterId,
            originalServers,
            DateTimeOffset.UtcNow);

        var result = await _elevatedLauncher.ExecuteAsync(request, cancellationToken);

        await _auditLog.RecordAsync(
            new PrivilegedOperationAuditEntry(DateTimeOffset.UtcNow, nameof(PrivilegedCapability.SetAdapterDnsServers), _adapterId, "Rollback", result.Succeeded, result.Output ?? result.Error ?? string.Empty),
            cancellationToken);

        return new OptimizationResult(
            Describe().Id,
            result.Succeeded ? OptimizationOutcome.RolledBack : OptimizationOutcome.Failed,
            DateTimeOffset.UtcNow,
            result.Output,
            result.Succeeded ? null : result.Error);
    }
}
