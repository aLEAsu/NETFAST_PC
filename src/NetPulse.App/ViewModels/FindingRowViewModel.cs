using NetPulse.Application.UseCases;

namespace NetPulse.App.ViewModels;

public sealed class FindingRowViewModel
{
    public FindingRowViewModel(DiagnosticRunResult result)
    {
        var stats = result.Statistics;
        var finding = result.Finding;

        Target = stats.Target;
        Confidence = finding.Confidence.ToString();
        Summary = finding.Summary;
        Median = $"{stats.Median.TotalMilliseconds:F1} ms";
        P95 = $"{stats.P95.TotalMilliseconds:F1} ms";
        Jitter = $"{stats.JitterMeanAbsoluteDeviation.TotalMilliseconds:F1} ms";
        Loss = $"{stats.PacketLossPercent:F1}%";
        Limitations = finding.Limitations.Length > 0 ? string.Join(" | ", finding.Limitations) : "-";
    }

    public string Target { get; }
    public string Confidence { get; }
    public string Summary { get; }
    public string Median { get; }
    public string P95 { get; }
    public string Jitter { get; }
    public string Loss { get; }
    public string Limitations { get; }
}
