using System.Diagnostics;
using System.Net.Sockets;
using NetPulse.Application.Ports;
using NetPulse.Domain.Network;

namespace NetPulse.Infrastructure.Windows.Network;

/// <summary>
/// Attempts a single TCP handshake - no data is sent or received beyond the handshake
/// itself, and the connection is closed immediately after. This exists to answer exactly
/// one question ICMP cannot: is this target reachable at the application level, regardless
/// of whether ICMP echo is filtered along the way.
/// </summary>
public sealed class TcpConnectProbeService : ITcpConnectProbeService
{
    public async Task<TcpConnectSample> ProbeOnceAsync(string target, int port, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var timestamp = DateTimeOffset.UtcNow;
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(target, port, timeoutCts.Token);
            stopwatch.Stop();

            return new TcpConnectSample(target, port, timestamp, Connected: true, stopwatch.Elapsed, Error: null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Our own timeout fired, not the caller's cancellation - report as a normal
            // failed sample rather than letting cancellation propagate.
            return new TcpConnectSample(target, port, timestamp, Connected: false, ConnectTime: null, Error: "Connection attempt timed out.");
        }
        catch (SocketException ex)
        {
            return new TcpConnectSample(target, port, timestamp, Connected: false, ConnectTime: null, Error: ex.Message);
        }
    }
}
