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
        Confidence = finding.Confidence.ToString();
        Summary = finding.Summary;
        Median = $"{stats.Median.TotalMilliseconds:F1} ms";
        P95 = $"{stats.P95.TotalMilliseconds:F1} ms";
        Jitter = $"{stats.JitterMeanAbsoluteDeviation.TotalMilliseconds:F1} ms";
        Loss = $"{stats.PacketLossPercent:F1}%";
        Limitations = finding.Limitations.Length > 0 ? string.Join(" | ", finding.Limitations) : "-";

        TcpCheck = tcp.Connected
            ? $"Connected ({tcp.ConnectTime!.Value.TotalMilliseconds:F0} ms, port {tcp.Port})"
            : $"Failed (port {tcp.Port}): {tcp.Error}";

        DnsCheck = !dns.IsApplicable
            ? "N/A (target is an IP literal)"
            : dns.Resolved
                ? $"Resolved ({dns.ResolutionTime!.Value.TotalMilliseconds:F0} ms)"
                : $"Failed: {dns.Error}";
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
