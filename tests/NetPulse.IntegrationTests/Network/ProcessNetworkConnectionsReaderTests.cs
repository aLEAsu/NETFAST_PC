using System.Net.Sockets;
using NetPulse.Infrastructure.Windows.Network;

namespace NetPulse.IntegrationTests.Network;

/// <summary>
/// Exercises the real IP Helper API call (GetExtendedTcpTable) against this machine - no
/// fakes, no elevation (verified: this call succeeds as a standard user, unlike pktmon).
/// </summary>
public class ProcessNetworkConnectionsReaderTests
{
    private static readonly ProcessNetworkConnectionsReader Reader = new();

    [Fact]
    public async Task GetActiveConnectionsAsync_DoesNotThrow_AndReturnsAList()
    {
        var connections = await Reader.GetActiveConnectionsAsync(CancellationToken.None);

        Assert.NotNull(connections);
    }

    [Fact]
    public async Task GetActiveConnectionsAsync_EveryConnection_HasAWellFormedStateAndEndpoints()
    {
        var connections = await Reader.GetActiveConnectionsAsync(CancellationToken.None);

        Assert.All(connections, c =>
        {
            Assert.False(string.IsNullOrWhiteSpace(c.ProcessName));
            Assert.False(string.IsNullOrWhiteSpace(c.State));
            Assert.InRange(c.LocalPort, 0, 65535);
            Assert.InRange(c.RemotePort, 0, 65535);
        });
    }

    [Fact]
    public async Task GetActiveConnectionsAsync_DetectsAConnectionThisTestItselfOpens()
    {
        // Open a real, throwaway loopback TCP connection and confirm the reader actually
        // observes it - not just that it returns an empty, trivially-true list.
        using var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;

        using var client = new TcpClient();
        await client.ConnectAsync(System.Net.IPAddress.Loopback, port);
        using var accepted = await listener.AcceptTcpClientAsync();

        var connections = await Reader.GetActiveConnectionsAsync(CancellationToken.None);

        Assert.Contains(connections, c =>
            c.ProcessId == Environment.ProcessId &&
            c.RemotePort == port &&
            c.RemoteAddress == "127.0.0.1");
    }

    [Fact]
    public async Task GetActiveConnectionsAsync_CalledTwiceInARow_DoesNotThrow()
    {
        var first = await Reader.GetActiveConnectionsAsync(CancellationToken.None);
        var second = await Reader.GetActiveConnectionsAsync(CancellationToken.None);

        Assert.NotNull(first);
        Assert.NotNull(second);
    }
}
