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
                    Summary: $"{stats.Target} did not respond to ICMP, but a direct TCP connection on port {tcpCheck.Port} succeeded in {tcpCheck.ConnectTime!.Value.TotalMilliseconds:F0} ms.",
                    Confidence: DiagnosticConfidence.Healthy,
                    Evidence: $"{stats.SampleCount} ICMP probes unanswered; TCP handshake to port {tcpCheck.Port} succeeded.",
                    Limitations: new[] { "ICMP being filtered does not indicate a problem by itself - many networks and firewalls block it deliberately while allowing normal application traffic." });
            }

            return new DiagnosticFinding(
                Id: "connectivity.no-response",
                Summary: $"No replies were received from {stats.Target}, and a direct TCP connection on port {tcpCheck.Port} also failed.",
                Confidence: DiagnosticConfidence.Probable,
                Evidence: $"{stats.SampleCount} ICMP probes sent, 0 responses. TCP connect to port {tcpCheck.Port}: {tcpCheck.Error}",
                Limitations: new[]
                {
                    "Two independent methods agreeing raises confidence but does not by itself confirm the cause (this PC vs. router vs. ISP vs. remote target).",
                    $"A failed TCP connection on port {tcpCheck.Port} specifically means that port didn't answer - it does not rule out the target being reachable on a different port.",
                });
        }

        if (stats.SampleCount < minimumSamplesForConfidence)
        {
            return new DiagnosticFinding(
                Id: "connectivity.insufficient-samples",
                Summary: $"Only {stats.SampleCount} samples were collected for {stats.Target}.",
                Confidence: DiagnosticConfidence.Hypothesis,
                Evidence: "Sample count is below the threshold needed to distinguish a real effect from measurement noise.",
                Limitations: new[] { "Increase SampleCount and re-run before treating this result as conclusive." });
        }

        if (stats.PacketLossPercent >= 2.0)
        {
            return new DiagnosticFinding(
                Id: "connectivity.packet-loss",
                Summary: $"{stats.PacketLossPercent:F1}% of probes to {stats.Target} received no reply.",
                Confidence: DiagnosticConfidence.Probable,
                Evidence: $"{stats.SampleCount - stats.RespondedCount} of {stats.SampleCount} probes unanswered. TCP connect to port {tcpCheck.Port}: {(tcpCheck.Connected ? "succeeded" : "also failed")}.",
                Limitations: new[] { "Measures ICMP reachability, not confirmed loss of this machine's own application traffic.", "May originate from the router, ISP, or remote target rather than this PC." });
        }

        if (stats.JitterMeanAbsoluteDeviation > TimeSpan.FromMilliseconds(30))
        {
            return new DiagnosticFinding(
                Id: "connectivity.high-jitter",
                Summary: $"Latency to {stats.Target} varies by a mean of {stats.JitterMeanAbsoluteDeviation.TotalMilliseconds:F1} ms between consecutive samples.",
                Confidence: DiagnosticConfidence.Probable,
                Evidence: $"Median {stats.Median.TotalMilliseconds:F1} ms, P95 {stats.P95.TotalMilliseconds:F1} ms, max {stats.Max.TotalMilliseconds:F1} ms.",
                Limitations: new[] { "High variability alone does not identify the cause; compare against a bufferbloat test under load before attributing it to the local network." });
        }

        return new DiagnosticFinding(
            Id: "connectivity.healthy",
            Summary: $"Latency to {stats.Target} is stable: median {stats.Median.TotalMilliseconds:F1} ms, {stats.PacketLossPercent:F1}% loss.",
            Confidence: DiagnosticConfidence.Healthy,
            Evidence: $"{stats.SampleCount} samples, jitter {stats.JitterMeanAbsoluteDeviation.TotalMilliseconds:F1} ms.",
            Limitations: new[] { "Reflects only this target and this time window; it is not a complete assessment of internet quality." });
    }
}

public sealed record DiagnosticRunResult(
    LatencyStatistics Statistics,
    DiagnosticFinding Finding,
    TcpConnectSample TcpCheck,
    DnsResolutionSample DnsCheck);
