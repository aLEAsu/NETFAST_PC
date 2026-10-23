using NetPulse.App.ViewModels;
using NetPulse.Application.UseCases;
using NetPulse.Domain.Diagnostics;
using NetPulse.Domain.Network;

namespace NetPulse.IntegrationTests.App;

/// <summary>
/// NetPulse.App has no automated coverage otherwise (it's the WPF presentation layer) - this
/// verifies the one piece of real logic added to it in the Priority 2 completion pass: the
/// TCP/DNS result formatting shown in the dashboard's findings grid. Lives here (not in
/// NetPulse.UnitTests) because NetPulse.App targets net10.0-windows and NetPulse.UnitTests
/// targets plain net10.0, which cannot reference a Windows-targeted project.
/// </summary>
public class FindingRowViewModelTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static LatencyStatistics Stats(string target) => new(
        target, 20, 20, 0.0, TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(20),
        TimeSpan.FromMilliseconds(25), TimeSpan.FromMilliseconds(30), TimeSpan.FromMilliseconds(3), Now, Now);

    private static DiagnosticFinding Finding() =>
        new("test", "summary", DiagnosticConfidence.Healthy, "evidence", Array.Empty<string>());

    [Fact]
    public void TcpCheck_Connected_ShowsConnectTimeAndPort()
    {
        var tcp = new TcpConnectSample("1.1.1.1", 443, Now, Connected: true, TimeSpan.FromMilliseconds(12), Error: null);
        var dns = DnsResolutionSample.NotApplicable("1.1.1.1");
        var row = new FindingRowViewModel(new DiagnosticRunResult(Stats("1.1.1.1"), Finding(), tcp, dns));

        Assert.Contains("12", row.TcpCheck);
        Assert.Contains("443", row.TcpCheck);
        Assert.Contains("Conectado", row.TcpCheck);
    }

    [Fact]
    public void TcpCheck_Failed_ShowsErrorAndPortWithoutThrowing()
    {
        var tcp = new TcpConnectSample("203.0.113.1", 443, Now, Connected: false, ConnectTime: null, Error: "Connection timed out");
        var dns = DnsResolutionSample.NotApplicable("203.0.113.1");
        var row = new FindingRowViewModel(new DiagnosticRunResult(Stats("203.0.113.1"), Finding(), tcp, dns));

        Assert.Contains("Falló", row.TcpCheck);
        Assert.Contains("Connection timed out", row.TcpCheck);
        Assert.Contains("443", row.TcpCheck);
    }

    [Fact]
    public void DnsCheck_NotApplicable_SaysSoRatherThanLookingLikeAFailure()
    {
        var tcp = new TcpConnectSample("1.1.1.1", 443, Now, Connected: true, TimeSpan.FromMilliseconds(5), Error: null);
        var dns = DnsResolutionSample.NotApplicable("1.1.1.1");
        var row = new FindingRowViewModel(new DiagnosticRunResult(Stats("1.1.1.1"), Finding(), tcp, dns));

        Assert.Contains("N/A", row.DnsCheck);
        Assert.DoesNotContain("Falló", row.DnsCheck);
    }

    [Fact]
    public void DnsCheck_Resolved_ShowsResolutionTime()
    {
        var tcp = new TcpConnectSample("example.com", 443, Now, Connected: true, TimeSpan.FromMilliseconds(5), Error: null);
        var dns = new DnsResolutionSample("example.com", Now, Resolved: true, TimeSpan.FromMilliseconds(45), new[] { "1.2.3.4" }, Error: null);
        var row = new FindingRowViewModel(new DiagnosticRunResult(Stats("example.com"), Finding(), tcp, dns));

        Assert.Contains("45", row.DnsCheck);
        Assert.Contains("Resuelto", row.DnsCheck);
    }

    [Fact]
    public void DnsCheck_Failed_ShowsErrorWithoutThrowing()
    {
        var tcp = new TcpConnectSample("bad.example", 443, Now, Connected: true, TimeSpan.FromMilliseconds(5), Error: null);
        var dns = new DnsResolutionSample("bad.example", Now, Resolved: false, ResolutionTime: null, Array.Empty<string>(), Error: "Name resolution failed");
        var row = new FindingRowViewModel(new DiagnosticRunResult(Stats("bad.example"), Finding(), tcp, dns));

        Assert.Contains("Falló", row.DnsCheck);
        Assert.Contains("Name resolution failed", row.DnsCheck);
    }
}
