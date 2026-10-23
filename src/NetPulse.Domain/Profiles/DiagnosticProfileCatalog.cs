namespace NetPulse.Domain.Profiles;

/// <summary>
/// The fixed, reviewed set of profiles this build ships with - same closed-catalog approach
/// as DefaultRecommendationRules, for the same reason: predictable, auditable behavior
/// rather than arbitrary configuration. Every profile here only adjusts measurement
/// parameters and result ordering; none of them changes what counts as healthy/unhealthy
/// (that stays in ConnectionDiagnosticUseCase.Classify and the recommendation rules,
/// unaffected by which profile is selected) and none applies a system change by itself.
///
/// Honesty note: "Gaming" and "VideoCalls" prioritize jitter/loss findings because those are
/// what this build can actually measure (ICMP + one TCP/DNS check) and that genuinely matter
/// for those use cases. Neither profile unlocks a throughput test, a stability-over-time
/// monitor, or anything else not implemented elsewhere in this codebase - a profile cannot
/// make an unimplemented capability appear.
/// </summary>
public static class DiagnosticProfileCatalog
{
    public static DiagnosticProfileSettings Get(DiagnosticProfile profile) => profile switch
    {
        DiagnosticProfile.General => new(
            DiagnosticProfile.General,
            "General",
            "Equilibrio para uso cotidiano - ningún hallazgo en particular se prioriza sobre otro.",
            SampleCount: 20,
            IntervalBetweenSamples: TimeSpan.FromMilliseconds(200),
            ProbeTimeout: TimeSpan.FromSeconds(2),
            PriorityRecommendationIds: Array.Empty<string>()),

        DiagnosticProfile.Gaming => new(
            DiagnosticProfile.Gaming,
            "Juegos / baja latencia",
            "Más muestras para detectar mejor el jitter, ya que la consistencia importa más que el throughput en tiempo real. Los hallazgos de jitter y latencia se muestran primero.",
            SampleCount: 30,
            IntervalBetweenSamples: TimeSpan.FromMilliseconds(100),
            ProbeTimeout: TimeSpan.FromSeconds(1),
            PriorityRecommendationIds: new[] { "recommend.high-jitter-bufferbloat-followup", "recommend.high-median-latency" }),

        DiagnosticProfile.Downloads => new(
            DiagnosticProfile.Downloads,
            "Descargas / rendimiento de transferencia",
            "Muestreo estándar - los hallazgos de pérdida de paquetes se muestran primero, ya que la pérdida afecta más al throughput y a los reintentos que el jitter.",
            SampleCount: 20,
            IntervalBetweenSamples: TimeSpan.FromMilliseconds(200),
            ProbeTimeout: TimeSpan.FromSeconds(2),
            PriorityRecommendationIds: new[] { "recommend.packet-loss-investigate" }),

        DiagnosticProfile.VideoCalls => new(
            DiagnosticProfile.VideoCalls,
            "Videollamadas / estabilidad",
            "Más muestras, con menor espaciado, para detectar mejor los picos breves de jitter/pérdida que causan cortes en las llamadas. Ambos se muestran primero.",
            SampleCount: 30,
            IntervalBetweenSamples: TimeSpan.FromMilliseconds(150),
            ProbeTimeout: TimeSpan.FromSeconds(1),
            PriorityRecommendationIds: new[] { "recommend.high-jitter-bufferbloat-followup", "recommend.packet-loss-investigate" }),

        DiagnosticProfile.Conservative => new(
            DiagnosticProfile.Conservative,
            "Conservador",
            "Menos muestras y mayor espaciado, para minimizar el tráfico que el propio diagnóstico genera. Los resultados son menos confiables estadísticamente como contrapartida directa.",
            SampleCount: 10,
            IntervalBetweenSamples: TimeSpan.FromMilliseconds(300),
            ProbeTimeout: TimeSpan.FromSeconds(2),
            PriorityRecommendationIds: Array.Empty<string>()),

        DiagnosticProfile.Maintenance => new(
            DiagnosticProfile.Maintenance,
            "Mantenimiento / diagnóstico a fondo",
            "Más muestras que General, para una comprobación puntual con mayor confianza estadística - pensado para investigar un problema deliberadamente, no para uso frecuente/rutinario.",
            SampleCount: 50,
            IntervalBetweenSamples: TimeSpan.FromMilliseconds(100),
            ProbeTimeout: TimeSpan.FromSeconds(2),
            PriorityRecommendationIds: Array.Empty<string>()),

        _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, "Unknown profile."),
    };

    public static IReadOnlyList<DiagnosticProfile> All { get; } = Enum.GetValues<DiagnosticProfile>();
}
