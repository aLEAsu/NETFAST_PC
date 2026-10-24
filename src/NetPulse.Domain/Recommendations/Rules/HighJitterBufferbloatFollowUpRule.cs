using NetPulse.Domain.Diagnostics;
using NetPulse.Domain.Optimization;

namespace NetPulse.Domain.Recommendations.Rules;

/// <summary>
/// High jitter at rest is a hint, not proof, of bufferbloat/congestion - the brief requires
/// a dedicated latency-under-load comparison (§3.5 of the brief's §E) before concluding
/// that. This rule only ever recommends running that follow-up test - Module E
/// (<c>BufferbloatUseCase</c>, the "Bufferbloat" tab) now implements it for real.
/// </summary>
public sealed class HighJitterBufferbloatFollowUpRule : IRecommendationRule
{
    private static readonly TimeSpan JitterThreshold = TimeSpan.FromMilliseconds(30);

    public OptimizationDescriptor Descriptor { get; } = new(
        Id: "recommend.high-jitter-bufferbloat-followup",
        Title: "Ejecutar una prueba de bufferbloat (latencia bajo carga) antes de sacar conclusiones",
        Objective: "Confirmar o descartar el bufferbloat/congestión como causa de la variabilidad de latencia observada.",
        Evidence: "La latencia entre muestras consecutivas varía más que el umbral en reposo, lo cual es consistente con (pero no prueba de) bufferbloat.",
        Preconditions: new[] { $"Jitter (desviación media absoluta entre RTT consecutivos) > {JitterThreshold.TotalMilliseconds:F0} ms." },
        Risk: OptimizationRisk.Low,
        RequiredPermissions: Array.Empty<string>(),
        VerificationMethod: "Compara la latencia en reposo contra la latencia durante una carga controlada y consentida de bajada/subida - pestaña \"Bufferbloat\" de esta aplicación.",
        SuccessCriterion: "La latencia bajo carga no es sustancialmente mayor que en reposo, descartando bufferbloat local.",
        FailureCriterion: "La latencia bajo carga sube marcadamente, consistente con bufferbloat probablemente corregible mediante QoS/SQM en el router.",
        RollbackMethod: "No aplica - esta recomendación solo sugiere un diagnóstico de seguimiento.",
        ReferenceDocumentation: new[] { "Brief del proyecto, Módulo E (diagnóstico de bufferbloat): requiere una prueba de carga dedicada, no solo el jitter en reposo." },
        KnownLimitations: new[] { "Un jitter alto también puede venir de interferencia Wi-Fi o un destino remoto ocupado, no solo de encolamiento local." });

    public TriggeredRecommendation? Evaluate(NetworkEvidence evidence)
    {
        var (statistics, finding) = (evidence.Statistics, evidence.Finding);

        if (finding.Confidence == DiagnosticConfidence.Hypothesis)
            return null;

        if (statistics.JitterMeanAbsoluteDeviation <= JitterThreshold)
            return null;

        return new TriggeredRecommendation(
            statistics.Target,
            Descriptor,
            $"Jitter de {statistics.JitterMeanAbsoluteDeviation.TotalMilliseconds:F1} ms observado contra {statistics.Target} en reposo.",
            finding.Confidence);
    }
}
