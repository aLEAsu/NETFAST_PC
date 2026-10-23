using System.Net;
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
        Title: "Establecer servidores DNS en este adaptador",
        Objective: "Reemplazar los servidores DNS configurados del adaptador por el conjunto solicitado y validado.",
        Evidence: "Recomendación elegida por el usuario; esta acción no decide por sí misma que el DNS sea el problema.",
        Preconditions: new[] { "El adaptador existe.", "La lista de servidores solicitada pasa DnsServerAddressValidator." },
        Risk: OptimizationRisk.Medium,
        RequiredPermissions: new[] { "Elevación de administrador para esta única operación (control de UAC en el proceso auxiliar elevado)." },
        VerificationMethod: "Volver a leer la configuración DNS del adaptador después de aplicar y confirmar que coincide con los servidores solicitados.",
        SuccessCriterion: "La lista de servidores DNS del adaptador coincide exactamente con la solicitada después de aplicar.",
        FailureCriterion: "El proceso auxiliar elevado reporta fallo, o la lectura posterior no coincide con la lista solicitada.",
        RollbackMethod: "Volver a aplicar la lista de servidores DNS capturada antes de ejecutar esta acción, a través del mismo proceso auxiliar elevado.",
        ReferenceDocumentation: new[] { "Win32_NetworkAdapterConfiguration.SetDNSServerSearchOrder (WMI de Windows)." },
        KnownLimitations: new[]
        {
            "Cambiar el DNS no garantiza menor latencia ni menos errores; solo cambia qué resolutor se usa.",
            "Algunas redes sobrescriben el DNS a nivel de router/DHCP sin importar esta configuración.",
            "Requiere que el usuario apruebe un control de UAC para el proceso auxiliar elevado; esta acción no puede ni intenta evitarlo.",
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
        bool matches = DnsServerListsMatch(currentServers, _requestedDnsServers);

        await _auditLog.RecordAsync(
            new PrivilegedOperationAuditEntry(DateTimeOffset.UtcNow, nameof(PrivilegedCapability.SetAdapterDnsServers), _adapterId, "Verify", matches, $"Observed: {string.Join(",", currentServers)}"),
            cancellationToken);

        return new OptimizationResult(
            Describe().Id,
            matches ? OptimizationOutcome.VerifiedImproved : OptimizationOutcome.VerifiedInconclusive,
            DateTimeOffset.UtcNow,
            $"Servidores DNS observados: {string.Join(",", currentServers)}",
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

    /// <summary>
    /// Compares by parsed address value, not raw string, and in order. WMI can return an
    /// IPv6 literal in a different canonical form than the one the caller supplied (e.g.
    /// "::1" vs "0:0:0:0:0:0:0:1"); a textual SequenceEqual would wrongly report
    /// VerifiedInconclusive for a change that actually succeeded.
    /// </summary>
    private static bool DnsServerListsMatch(IReadOnlyList<string> observed, IReadOnlyList<string> requested)
    {
        if (observed.Count != requested.Count)
            return false;

        for (int i = 0; i < observed.Count; i++)
        {
            if (!IPAddress.TryParse(observed[i], out var observedAddress))
                return false;

            if (!IPAddress.TryParse(requested[i], out var requestedAddress))
                return false;

            if (!observedAddress.Equals(requestedAddress))
                return false;
        }

        return true;
    }
}
