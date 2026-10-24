using NetPulse.Domain.Diagnostics;
using NetPulse.Domain.Optimization;

namespace NetPulse.Domain.Recommendations.Rules;

/// <summary>
/// Flags consistently high latency even when loss and jitter are both low - a signal the
/// loss/jitter-only classification in ConnectionDiagnosticUseCase.Classify does not surface
/// on its own. Deliberately does not blame Windows: distance to the target, Wi-Fi vs.
/// Ethernet, and the ISP's own path are all more common causes than any local setting.
/// </summary>
public sealed class HighMedianLatencyRule : IRecommendationRule
{
    private static readonly TimeSpan MedianThreshold = TimeSpan.FromMilliseconds(100);

    public OptimizationDescriptor Descriptor { get; } = new(
        Id: "recommend.high-median-latency",
        Title: "Revisar Wi-Fi vs. Ethernet y la distancia al destino antes de asumir un problema corregible",
        Objective: "Evitar tratar la latencia normal por geografía/ruta como un defecto corregible con un cambio local.",
        Evidence: "El tiempo de ida y vuelta mediano hacia este destino es consistentemente alto aunque la pérdida y el jitter están dentro del rango normal.",
        Preconditions: new[] { $"RTT mediano > {MedianThreshold.TotalMilliseconds:F0} ms.", "La pérdida de paquetes y el jitter están ambos dentro del rango normal para esta ejecución." },
        Risk: OptimizationRisk.Low,
        RequiredPermissions: Array.Empty<string>(),
        VerificationMethod: "Compara contra una conexión cableada (si actualmente usas Wi-Fi) y contra un destino geográficamente más cercano.",
        SuccessCriterion: "Una conexión cableada o un destino más cercano muestra una latencia mediana notablemente menor, señalando la configuración original en vez de esta PC.",
        FailureCriterion: "La latencia se mantiene alta incluso por cable y contra un destino cercano, sugiriendo un problema del lado del ISP o de enrutamiento fuera del control de esta PC.",
        RollbackMethod: "No aplica - esta recomendación solo sugiere una medición comparativa.",
        ReferenceDocumentation: new[] { "Brief del proyecto §2: distinguir lo que está bajo el control de la PC de lo que depende del router/ISP/infraestructura remota." },
        KnownLimitations: new[] { "No existe un número universal de latencia 'buena'; el punto de referencia correcto depende de la distancia física al destino." });

    public TriggeredRecommendation? Evaluate(NetworkEvidence evidence)
    {
        var (statistics, finding) = (evidence.Statistics, evidence.Finding);

        if (finding.Confidence == DiagnosticConfidence.Hypothesis)
            return null;

        if (statistics.Median <= MedianThreshold)
            return null;

        return new TriggeredRecommendation(
            statistics.Target,
            Descriptor,
            $"La latencia mediana hacia {statistics.Target} es {statistics.Median.TotalMilliseconds:F1} ms con {statistics.PacketLossPercent:F1}% de pérdida.",
            finding.Confidence);
    }
}
