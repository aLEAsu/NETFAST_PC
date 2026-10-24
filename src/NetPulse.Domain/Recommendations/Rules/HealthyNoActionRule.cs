using NetPulse.Domain.Diagnostics;
using NetPulse.Domain.Optimization;

namespace NetPulse.Domain.Recommendations.Rules;

/// <summary>
/// Explicitly recommends doing nothing when the measured state is already healthy, per the
/// brief's requirement that a profile/engine be allowed to recommend no change at all
/// (§7, §H). This exists so "no recommendations fired" is never silently indistinguishable
/// from "nothing was checked."
/// </summary>
public sealed class HealthyNoActionRule : IRecommendationRule
{
    public OptimizationDescriptor Descriptor { get; } = new(
        Id: "recommend.healthy-no-action",
        Title: "No se recomienda ningún cambio",
        Objective: "Evitar cambiar una configuración que no está causando un problema medido.",
        Evidence: "La latencia, el jitter y la pérdida de paquetes están todos dentro del rango normal medido para este destino.",
        Preconditions: new[] { "La confianza del hallazgo de diagnóstico es Saludable." },
        Risk: OptimizationRisk.Low,
        RequiredPermissions: Array.Empty<string>(),
        VerificationMethod: "Vuelve a ejecutar el diagnóstico periódicamente; solo reevalúa si un nuevo diagnóstico muestra pérdida, jitter o latencia mediana fuera del rango normal.",
        SuccessCriterion: "No aplica - no hay nada que verificar porque no se propone ningún cambio.",
        FailureCriterion: "No aplica.",
        RollbackMethod: "No aplica - no se propone ningún cambio.",
        ReferenceDocumentation: new[] { "Brief del proyecto §7 y §H: un perfil puede recomendar no hacer ningún cambio cuando el estado actual ya es adecuado." },
        KnownLimitations: new[] { "Refleja solo el destino y la ventana de tiempo recién medidos, no es una garantía de estabilidad futura." });

    public TriggeredRecommendation? Evaluate(NetworkEvidence evidence)
    {
        var (statistics, finding) = (evidence.Statistics, evidence.Finding);

        if (finding.Confidence != DiagnosticConfidence.Healthy)
            return null;

        return new TriggeredRecommendation(
            statistics.Target,
            Descriptor,
            $"{statistics.Target} está dentro del rango normal: mediana {statistics.Median.TotalMilliseconds:F1} ms, {statistics.PacketLossPercent:F1}% de pérdida, jitter {statistics.JitterMeanAbsoluteDeviation.TotalMilliseconds:F1} ms.",
            finding.Confidence);
    }
}
