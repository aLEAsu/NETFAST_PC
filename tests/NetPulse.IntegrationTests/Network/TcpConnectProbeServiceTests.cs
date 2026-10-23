using System.Net;
using System.Net.Sockets;
using NetPulse.Infrastructure.Windows.Network;

namespace NetPulse.IntegrationTests.Network;

/// <summary>
/// Real TCP connects - no fakes. Success is tested against a throwaway local
/// <see cref="TcpListener"/> on an ephemeral port (self-contained, no dependency on any
/// external host being reachable); failure is tested against the same RFC 5737 TEST-NET-3
/// reserved address the rest of this suite already uses for a guaranteed non-response.
/// </summary>
public class TcpConnectProbeServiceTests
{
    private static readonly TcpConnectProbeService Service = new();

    [Fact]
    public async Task ProbeOnceAsync_RealListeningPort_Connects()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var acceptTask = listener.AcceptTcpClientAsync();

        var sample = await Service.ProbeOnceAsync("127.0.0.1", port, TimeSpan.FromSeconds(2), CancellationToken.None);

        Assert.True(sample.Connected);
        Assert.NotNull(sample.ConnectTime);
        Assert.Null(sample.Error);

        using var accepted = await acceptTask;
        listener.Stop();
    }

    [Fact]
    public async Task ProbeOnceAsync_NothingListeningOnLoopbackPort_Fails()
    {
        // Nothing is listening on this ephemeral port - the OS should refuse the connection
        // quickly (not time out), which is itself useful signal the real app can rely on.
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop(); // freed immediately - nothing listens there now

        var sample = await Service.ProbeOnceAsync("127.0.0.1", port, TimeSpan.FromSeconds(2), CancellationToken.None);

        Assert.False(sample.Connected);
        Assert.NotNull(sample.Error);
    }

    [Fact]
    public async Task ProbeOnceAsync_ReservedNonRoutableAddress_FailsWithinTheTimeout()
    {
        var sample = await Service.ProbeOnceAsync("203.0.113.1", 443, TimeSpan.FromMilliseconds(800), CancellationToken.None);

        Assert.False(sample.Connected);
        Assert.Null(sample.ConnectTime);
    }

    [Fact]
    public async Task ProbeOnceAsync_CancelledToken_ThrowsOperationCancelled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Service.ProbeOnceAsync("127.0.0.1", 443, TimeSpan.FromSeconds(2), cts.Token));
    }
}
