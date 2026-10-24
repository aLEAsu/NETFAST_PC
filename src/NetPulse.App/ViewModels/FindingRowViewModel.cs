using NetPulse.Application.UseCases;

namespace NetPulse.App.ViewModels;

public sealed class FindingRowViewModel
{
    public FindingRowViewModel(DiagnosticRunResult result)
    {
        var stats = result.Statistics;
        var finding = result.Finding;
        var tcp = result.TcpCheck;
        var dns = result.DnsCheck;

        Target = stats.Target;
        Confidence = SpanishDisplayNames.Confidence(finding.Confidence);
        // Summary/Limitations come straight from ConnectionDiagnosticUseCase.Classify
        // (Domain layer) - now in Spanish there too; see that file's own history for why
        // this was previously deferred (it turned out only finding.Id was test-asserted,
        // never this prose, so the translation pass was much smaller than expected).
        Summary = finding.Summary;
        Median = $"{stats.Median.TotalMilliseconds:F1} ms";
        P95 = $"{stats.P95.TotalMilliseconds:F1} ms";
        Jitter = $"{stats.JitterMeanAbsoluteDeviation.TotalMilliseconds:F1} ms";
        Loss = $"{stats.PacketLossPercent:F1}%";
        Limitations = finding.Limitations.Length > 0 ? string.Join(" | ", finding.Limitations) : "-";

        TcpCheck = tcp.Connected
            ? $"Conectado ({tcp.ConnectTime!.Value.TotalMilliseconds:F0} ms, puerto {tcp.Port})"
            : $"Falló (puerto {tcp.Port}): {tcp.Error}";

        DnsCheck = !dns.IsApplicable
            ? "N/A (el objetivo es una IP literal)"
            : dns.Resolved
                ? $"Resuelto ({dns.ResolutionTime!.Value.TotalMilliseconds:F0} ms)"
                : $"Falló: {dns.Error}";
    }

    public string Target { get; }
    public string Confidence { get; }
    public string Summary { get; }
    public string Median { get; }
    public string P95 { get; }
    public string Jitter { get; }
    public string Loss { get; }
    public string TcpCheck { get; }
    public string DnsCheck { get; }
    public string Limitations { get; }
}
