using System.Net;
using NetPulse.Application.Ports;
using NetPulse.Domain.Diagnostics;
using NetPulse.Domain.Network;

namespace NetPulse.Application.UseCases;

/// <summary>
/// Orchestrates a bounded latency diagnostic run against the configured targets and
/// classifies the result per the confidence model in <see cref="DiagnosticConfidence"/>.
/// Contains no Windows- or transport-specific code: it only talks to the ports, so it can
/// be unit-tested with fakes.
/// </summary>
public sealed class ConnectionDiagnosticUseCase
{
    private readonly ILatencyProbeService _probeService;
    private readonly ITcpConnectProbeService _tcpProbeService;
    private readonly IDnsResolutionProbeService _dnsProbeService;
    private readonly IDiagnosticHistoryRepository _historyRepository;

    public ConnectionDiagnosticUseCase(
        ILatencyProbeService probeService,
        ITcpConnectProbeService tcpProbeService,
        IDnsResolutionProbeService dnsProbeService,
        IDiagnosticHistoryRepository historyRepository)
    {
        _probeService = probeService;
        _tcpProbeService = tcpProbeService;
        _dnsProbeService = dnsProbeService;
        _historyRepository = historyRepository;
    }

    public async Task<IReadOnlyList<DiagnosticRunResult>> RunAsync(DiagnosticOptions options, CancellationToken cancellationToken)
    {
        if (options.Targets.Count == 0)
            throw new ArgumentException("At least one target is required.", nameof(options));

        var results = new List<DiagnosticRunResult>();

        foreach (var target in options.Targets)
        {
            var samples = new List<LatencySample>(options.SampleCount);

            for (int i = 0; i < options.SampleCount; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var sample = await _probeService.ProbeOnceAsync(target, options.EffectiveTimeout, cancellationToken);
                samples.Add(sample);

                if (i < options.SampleCount - 1)
                    await Task.Delay(options.EffectiveInterval, cancellationToken);
            }

            var statistics = LatencyStatistics.From(target, samples);
            await _historyRepository.SaveAsync(statistics, cancellationToken);

            // One confirmatory TCP attempt per target - not repeated like ICMP sampling, see
            // TcpConnectSample's doc comment for why. This exists specifically to answer what
            // ICMP alone cannot: is the target reachable at the application level.
            var tcpCheck = await _tcpProbeService.ProbeOnceAsync(target, options.TcpConfirmationPort, options.EffectiveTimeout, cancellationToken);

            // DNS resolution only applies to a hostname target - an IP literal has nothing to
            // resolve, and probing it anyway would misleadingly read as "a lookup that failed"
            // rather than "not applicable."
            var dnsCheck = IPAddress.TryParse(target, out _)
                ? DnsResolutionSample.NotApplicable(target)
                : await _dnsProbeService.ProbeOnceAsync(target, options.EffectiveTimeout, cancellationToken);

            var finding = Classify(statistics, tcpCheck);
            results.Add(new DiagnosticRunResult(statistics, finding, tcpCheck, dnsCheck));
        }

        return results;
    }

    /// <summary>
    /// Deterministic, documented thresholds - not a universal "good ping" number. These exist
    /// so the UI has something to show immediately; the project's recommendation engine
    /// (Phase 4) is expected to refine this with historical baselines and variability analysis
    /// rather than fixed cutoffs.
    /// </summary>
    internal static DiagnosticFinding Classify(LatencyStatistics stats, TcpConnectSample tcpCheck)
    {
        const int minimumSamplesForConfidence = 10;

        if (stats.RespondedCount == 0)
        {
            if (tcpCheck.Connected)
            {
                // ICMP alone cannot tell "filtered" from "down" - a successful TCP
                // handshake on the same target, in the same run, settles it: the target is
                // reachable at the application level, so ICMP's own silence is not a
                // connectivity problem by itself.
                return new DiagnosticFinding(
                    Id: "connectivity.icmp-filtered-app-reachable",
                    Summary: $"{stats.Target} no respondió a ICMP, pero una conexión TCP directa al puerto {tcpCheck.Port} tuvo éxito en {tcpCheck.ConnectTime!.Value.TotalMilliseconds:F0} ms.",
                    Confidence: DiagnosticConfidence.Healthy,
                    Evidence: $"{stats.SampleCount} sondeos ICMP sin respuesta; el enlace TCP al puerto {tcpCheck.Port} tuvo éxito.",
                    Limitations: new[] { "Que se filtre ICMP no indica un problema por sí solo - muchas redes y firewalls lo bloquean deliberadamente mientras permiten el tráfico normal de aplicaciones." });
            }

            return new DiagnosticFinding(
                Id: "connectivity.no-response",
                Summary: $"No se recibieron respuestas de {stats.Target}, y una conexión TCP directa al puerto {tcpCheck.Port} también falló.",
                Confidence: DiagnosticConfidence.Probable,
                Evidence: $"{stats.SampleCount} sondeos ICMP enviados, 0 respuestas. Conexión TCP al puerto {tcpCheck.Port}: {tcpCheck.Error}",
                Limitations: new[]
                {
                    "Que dos métodos independientes coincidan aumenta la confianza, pero no confirma por sí solo la causa (esta PC vs. router vs. ISP vs. destino remoto).",
                    $"Que falle la conexión TCP al puerto {tcpCheck.Port} significa específicamente que ese puerto no respondió - no descarta que el destino sea alcanzable por otro puerto.",
                });
        }

        if (stats.SampleCount < minimumSamplesForConfidence)
        {
            return new DiagnosticFinding(
                Id: "connectivity.insufficient-samples",
                Summary: $"Solo se recolectaron {stats.SampleCount} muestra(s) para {stats.Target}.",
                Confidence: DiagnosticConfidence.Hypothesis,
                Evidence: "La cantidad de muestras está por debajo del umbral necesario para distinguir un efecto real del ruido de medición.",
                Limitations: new[] { "Aumenta la cantidad de muestras y vuelve a ejecutar el diagnóstico antes de tratar este resultado como concluyente." });
        }

        if (stats.PacketLossPercent >= 2.0)
        {
            return new DiagnosticFinding(
                Id: "connectivity.packet-loss",
                Summary: $"{stats.PacketLossPercent:F1}% de los sondeos a {stats.Target} no recibieron respuesta.",
                Confidence: DiagnosticConfidence.Probable,
                Evidence: $"{stats.SampleCount - stats.RespondedCount} de {stats.SampleCount} sondeos sin respuesta. Conexión TCP al puerto {tcpCheck.Port}: {(tcpCheck.Connected ? "tuvo éxito" : "también falló")}.",
                Limitations: new[] { "Mide la alcanzabilidad por ICMP, no confirma pérdida del propio tráfico de aplicación de esta máquina.", "Puede originarse en el router, el ISP, o el destino remoto, no necesariamente en esta PC." });
        }

        if (stats.JitterMeanAbsoluteDeviation > TimeSpan.FromMilliseconds(30))
        {
            return new DiagnosticFinding(
                Id: "connectivity.high-jitter",
                Summary: $"La latencia hacia {stats.Target} varía en promedio {stats.JitterMeanAbsoluteDeviation.TotalMilliseconds:F1} ms entre muestras consecutivas.",
                Confidence: DiagnosticConfidence.Probable,
                Evidence: $"Mediana {stats.Median.TotalMilliseconds:F1} ms, P95 {stats.P95.TotalMilliseconds:F1} ms, máximo {stats.Max.TotalMilliseconds:F1} ms.",
                Limitations: new[] { "Una alta variabilidad por sí sola no identifica la causa; compara contra una prueba de bufferbloat bajo carga (pestaña \"Bufferbloat\") antes de atribuirla a la red local." });
        }

        return new DiagnosticFinding(
            Id: "connectivity.healthy",
            Summary: $"La latencia hacia {stats.Target} es estable: mediana {stats.Median.TotalMilliseconds:F1} ms, {stats.PacketLossPercent:F1}% de pérdida.",
            Confidence: DiagnosticConfidence.Healthy,
            Evidence: $"{stats.SampleCount} muestras, jitter {stats.JitterMeanAbsoluteDeviation.TotalMilliseconds:F1} ms.",
            Limitations: new[] { "Refleja solo este destino y esta ventana de tiempo; no es una evaluación completa de la calidad de internet." });
    }
}

public sealed record DiagnosticRunResult(
    LatencyStatistics Statistics,
    DiagnosticFinding Finding,
    TcpConnectSample TcpCheck,
    DnsResolutionSample DnsCheck);
