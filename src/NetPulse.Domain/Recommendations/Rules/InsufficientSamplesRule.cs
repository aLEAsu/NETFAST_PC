using NetPulse.Domain.Diagnostics;
using NetPulse.Domain.Optimization;

namespace NetPulse.Domain.Recommendations.Rules;

/// <summary>Never recommends a change - only recommends collecting more evidence first.</summary>
public sealed class InsufficientSamplesRule : IRecommendationRule
{
    public OptimizationDescriptor Descriptor { get; } = new(
        Id: "recommend.insufficient-samples",
        Title: "Recolectar más muestras antes de sacar conclusiones",
        Objective: "Evitar tratar el ruido de medición como un problema de red real.",
        Evidence: "Se recolectaron menos muestras que el mínimo necesario para distinguir un efecto real de la variación aleatoria.",
        Preconditions: new[] { "La confianza del hallazgo de diagnóstico es Hipótesis debido a la baja cantidad de muestras." },
        Risk: OptimizationRisk.Low,
        RequiredPermissions: Array.Empty<string>(),
        VerificationMethod: "Vuelve a ejecutar el diagnóstico con al menos 10 muestras contra el mismo destino.",
        SuccessCriterion: "La nueva ejecución produce un hallazgo Confirmado, Probable, Limitación externa, o Saludable en vez de Hipótesis.",
        FailureCriterion: "La nueva ejecución todavía tiene menos de 10 muestras (por ejemplo, se canceló antes de tiempo).",
        RollbackMethod: "No aplica - esta recomendación solo sugiere volver a ejecutar un diagnóstico de solo lectura.",
        ReferenceDocumentation: new[] { "Brief del proyecto §7: el motor no debe atribuir efectos sin muestras suficientes." },
        KnownLimitations: new[] { "No corrige nada por sí misma; solo mejora la confianza del próximo diagnóstico." });

    public TriggeredRecommendation? Evaluate(NetworkEvidence evidence)
    {
        var (statistics, finding) = (evidence.Statistics, evidence.Finding);

        if (finding.Confidence != DiagnosticConfidence.Hypothesis)
            return null;

        return new TriggeredRecommendation(
            statistics.Target,
            Descriptor,
            $"Solo se recolectaron {statistics.SampleCount} muestra(s) para {statistics.Target}; vuelve a ejecutar con más muestras antes de actuar sobre este resultado.",
            finding.Confidence);
    }
}
