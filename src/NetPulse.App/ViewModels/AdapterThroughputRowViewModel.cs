using NetPulse.Domain.Network;

namespace NetPulse.App.ViewModels;

/// <summary>Thin, read-only presentation wrapper - no behavior, just formatting for the grid.</summary>
public sealed class AdapterThroughputRowViewModel
{
    public AdapterThroughputRowViewModel(AdapterThroughputSample sample)
    {
        InterfaceName = sample.InterfaceName;
        BytesSentPerSecond = FormatRate(sample.BytesSentPerSecond);
        BytesReceivedPerSecond = FormatRate(sample.BytesReceivedPerSecond);
    }

    public string InterfaceName { get; }
    public string BytesSentPerSecond { get; }
    public string BytesReceivedPerSecond { get; }

    private static string FormatRate(double bytesPerSecond)
    {
        const double kb = 1024;
        const double mb = kb * 1024;
        return bytesPerSecond switch
        {
            >= mb => $"{bytesPerSecond / mb:F2} MB/s",
            >= kb => $"{bytesPerSecond / kb:F1} KB/s",
            _ => $"{bytesPerSecond:F0} B/s",
        };
    }
}
