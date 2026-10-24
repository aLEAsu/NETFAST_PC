using NetPulse.Domain.Diagnostics;
using NetPulse.Domain.Optimization;

namespace NetPulse.Domain.Recommendations.Rules;

/// <summary>
/// Fires when DNS resolution for a hostname target failed or was slow. This is the one
/// recommendation in this build with a real, applyable action behind it:
/// <c>NetPulse.PrivilegedAgent.SetAdapterDnsServersAction</c>. That action lives in a
/// project Domain cannot reference (it would be circular - PrivilegedAgent depends on
/// Domain), so the link is by convention, not by object reference: the UI layer recognizes
/// this rule's <see cref="OptimizationDescriptor.Id"/> and offers to run that action when it
/// fires. This rule itself never constructs or runs anything - it only observes evidence.
/// </summary>
public sealed class SlowDnsResolutionRule : IRecommendationRule
{
    public static readonly TimeSpan ResolutionTimeThreshold = TimeSpan.FromMilliseconds(100);

    public OptimizationDescriptor Descriptor { get; } = new(
        Id: "recommend.slow-dns-resolution",
        Title: "Probar un servidor DNS diferente",
        Objective: "Reducir fallos de resolución DNS o resoluciones lentas usando un resolver diferente.",
        Evidence: "La resolución DNS para este nombre de host falló o tardó más de lo esperado.",
        Preconditions: new[] { "El destino es un nombre de host, no una IP literal.", $"La resolución falló, o tardó más de {ResolutionTimeThreshold.TotalMilliseconds:F0} ms." },
        Risk: OptimizationRisk.Low,
        RequiredPermissions: Array.Empty<string>(),
        VerificationMethod: "Vuelve a resolver el mismo nombre de host después del cambio y compara el tiempo de resolución y el éxito.",
        SuccessCriterion: "La resolución tiene éxito y se completa más rápido que la medición original.",
        FailureCriterion: "La resolución sigue fallando o no es notablemente más rápida después del cambio.",
        RollbackMethod: "No aplica - esta recomendación en sí solo sugiere una comparación; si el usuario aplica la acción SetAdapterDnsServersAction subyacente, esa acción tiene su propia reversión independiente.",
        ReferenceDocumentation: new[] { "Brief del proyecto, Módulo D: los cambios de configuración DNS deben basarse en un problema diagnosticado, no aplicarse especulativamente." },
        KnownLimitations: new[]
        {
            "Una sola resolución lenta o fallida puede ser ruido transitorio de la red - confirma en más de una ejecución antes de concluir que el servidor DNS configurado es la causa.",
            "Cambiar de servidor DNS cambia qué resolver responde las consultas; no garantiza una resolución más rápida para todos los dominios, y algunas redes sobrescriben el DNS a nivel de router/DHCP sin importar esta configuración.",
        });

    public TriggeredRecommendation? Evaluate(NetworkEvidence evidence)
    {
        var dns = evidence.DnsCheck;

        if (!dns.IsApplicable)
            return null;

        bool slowOrFailed = !dns.Resolved || (dns.ResolutionTime is { } time && time > ResolutionTimeThreshold);
        if (!slowOrFailed)
            return null;

        var explanation = dns.Resolved
            ? $"La resolución DNS para {dns.Hostname} tardó {dns.ResolutionTime!.Value.TotalMilliseconds:F0} ms, por encima del umbral de {ResolutionTimeThreshold.TotalMilliseconds:F0} ms."
            : $"La resolución DNS para {dns.Hostname} falló: {dns.Error}";

        return new TriggeredRecommendation(evidence.Statistics.Target, Descriptor, explanation, evidence.Finding.Confidence);
    }
}
