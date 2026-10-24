using NetPulse.Domain.Diagnostics;
using NetPulse.Domain.Optimization;

namespace NetPulse.Domain.Recommendations.Rules;

/// <summary>
/// Significant packet loss is reported, but the rule deliberately does not propose a Windows
/// setting change: the brief requires that loss not be attributed to this PC before the
/// router, cable, driver, and ISP are ruled out (§2.6, §12).
/// </summary>
public sealed class PacketLossInvestigateRule : IRecommendationRule
{
    private const double LossThresholdPercent = 2.0;
    private const int MinimumSamplesForConfidence = 10;

    public OptimizationDescriptor Descriptor { get; } = new(
        Id: "recommend.packet-loss-investigate",
        Title: "Investigar la pérdida de paquetes fuera de Windows antes de cambiar cualquier configuración",
        Objective: "Descartar causas de router, cable, controlador e ISP antes de atribuir la pérdida a esta PC.",
        Evidence: "Una proporción significativa de sondeos ICMP no obtuvo respuesta, en suficientes muestras como para no ser ruido.",
        Preconditions: new[] { $"Pérdida de paquetes >= {LossThresholdPercent}%.", $"Se recolectaron al menos {MinimumSamplesForConfidence} muestras." },
        Risk: OptimizationRisk.Low,
        RequiredPermissions: Array.Empty<string>(),
        VerificationMethod: "Repite el diagnóstico evitando el Wi-Fi (cableado, si es posible), directamente contra la dirección del router, y después de reiniciar el router.",
        SuccessCriterion: "La pérdida baja al rango esperado una vez que se aíslan uno por uno los factores de router/cable/controlador mencionados arriba.",
        FailureCriterion: "La pérdida persiste incluso en una conexión cableada directa al router sin cambiar ninguna otra variable.",
        RollbackMethod: "No aplica - esta recomendación solo sugiere más diagnóstico, no un cambio del sistema.",
        ReferenceDocumentation: new[] { "Brief del proyecto §2.6 y §12: no atribuir la pérdida a la PC sin descartar causas de router/ISP/destino remoto." },
        KnownLimitations: new[] { "La pérdida por ICMP no confirma pérdida del propio tráfico de aplicación de esta máquina.", "El destino remoto o un salto intermedio puede ser la causa real." });

    public TriggeredRecommendation? Evaluate(NetworkEvidence evidence)
    {
        var (statistics, finding) = (evidence.Statistics, evidence.Finding);

        if (finding.Confidence == DiagnosticConfidence.Hypothesis)
            return null;

        if (statistics.PacketLossPercent < LossThresholdPercent)
            return null;

        return new TriggeredRecommendation(
            statistics.Target,
            Descriptor,
            $"{statistics.PacketLossPercent:F1}% de pérdida de paquetes observada contra {statistics.Target} en {statistics.SampleCount} muestras.",
            finding.Confidence);
    }
}
