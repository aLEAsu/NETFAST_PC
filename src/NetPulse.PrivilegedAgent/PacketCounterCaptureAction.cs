using NetPulse.Application.Ports;
using NetPulse.Domain.Optimization;
using NetPulse.Domain.PrivilegedOperations;

namespace NetPulse.PrivilegedAgent;

/// <summary>
/// Module G (brief §6.3), scoped down deliberately: runs a bounded, elevated pktmon
/// counters-only session (packet/drop COUNTS per network adapter, never packet content or
/// payload) and returns its raw text output unparsed - this project does not derive or
/// summarize a number from pktmon's output that it has not directly observed. Not an
/// <see cref="IOptimizationAction"/>: a capture is a one-shot, bounded read, not a
/// persistent setting with apply/verify/rollback semantics, so this exposes only
/// <see cref="Describe"/> and <see cref="CaptureAsync"/>. It reuses
/// <see cref="OptimizationDescriptor"/> anyway (rather than inventing a parallel type) so the
/// dashboard's existing confirmation-dialog rendering works unchanged for this action too.
///
/// Like <see cref="SetAdapterDnsServersAction"/>, this class has never been exercised
/// against a real elevated pktmon session by the automated session that wrote it - every
/// pktmon command here needs administrator rights (verified: even `pktmon list` refuses
/// unelevated), so its first real run must be a deliberate, manual action by the person
/// using the finished application.
/// </summary>
public sealed class PacketCounterCaptureAction
{
    private readonly IElevatedOperationLauncher _elevatedLauncher;
    private readonly IPrivilegedOperationAuditLog _auditLog;

    public PacketCounterCaptureAction(IElevatedOperationLauncher elevatedLauncher, IPrivilegedOperationAuditLog auditLog)
    {
        _elevatedLauncher = elevatedLauncher;
        _auditLog = auditLog;
    }

    public OptimizationDescriptor Describe() => new(
        Id: "action.capture-packet-counters",
        Title: "Capturar contadores de paquetes (pktmon)",
        Objective: "Contar paquetes y descartes por adaptador de red durante un tiempo acotado, usando la herramienta pktmon incluida en Windows.",
        Evidence: "Solicitud explícita del usuario desde la pestaña de captura avanzada; esta acción no decide por sí misma que haya un problema.",
        Preconditions: new[] { "pktmon.exe está disponible en este Windows (incluido desde Windows 10 1809 / Windows 11).", "La duración solicitada está entre 1 y 60 segundos." },
        Risk: OptimizationRisk.Medium,
        RequiredPermissions: new[] { "Elevación de administrador para esta única operación (control de UAC en el proceso auxiliar elevado) - pktmon rechaza incluso una consulta de solo lectura sin privilegios de administrador." },
        VerificationMethod: "El propio resultado de 'pktmon counters' se muestra sin procesar - no hay una verificación posterior separada, porque no se aplica ningún cambio persistente que verificar.",
        SuccessCriterion: "El proceso elevado devuelve la salida de 'pktmon counters' sin error.",
        FailureCriterion: "El proceso elevado devuelve un error (incluido que el usuario cancele el control de UAC), o pktmon no está disponible en este Windows.",
        RollbackMethod: "No aplica: el controlador de pktmon se descarga ('pktmon unload') al finalizar la captura, con o sin error, y no queda ningún cambio persistente que revertir.",
        ReferenceDocumentation: new[] { "https://learn.microsoft.com/windows-server/networking/technologies/pktmon/pktmon" },
        KnownLimitations: new[]
        {
            "Nunca captura contenido ni carga útil de los paquetes - solo conteos agregados por adaptador (modo '--counters-only').",
            "Cuenta todos los adaptadores de red juntos ('--comp nics'), no un adaptador específico - acotar a un adaptador concreto requeriría una enumeración elevada adicional ('pktmon list'), fuera del alcance de esta primera versión.",
            "La duración máxima de 60 segundos se aplica tanto aquí como, de forma independiente, dentro del proceso elevado.",
        });

    public async Task<PacketCaptureResult> CaptureAsync(int durationSeconds, CancellationToken cancellationToken)
    {
        var request = new PrivilegedOperationRequest(
            PrivilegedCapability.CapturePacketCounters,
            AdapterId: string.Empty, // not applicable - this capability is not scoped to one adapter
            Parameters: new[] { durationSeconds.ToString() },
            DateTimeOffset.UtcNow);

        var result = await _elevatedLauncher.ExecuteAsync(request, cancellationToken);

        await _auditLog.RecordAsync(
            new PrivilegedOperationAuditEntry(
                DateTimeOffset.UtcNow,
                nameof(PrivilegedCapability.CapturePacketCounters),
                "(all NICs)",
                "Capture",
                result.Succeeded,
                result.Output ?? result.Error ?? string.Empty),
            cancellationToken);

        return new PacketCaptureResult(result.Succeeded, result.Output, result.Error);
    }
}

public sealed record PacketCaptureResult(bool Succeeded, string? RawOutput, string? Error);
