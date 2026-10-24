using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using NetPulse.Application.Ports;
using NetPulse.Domain.Network;

namespace NetPulse.Infrastructure.Windows.Network;

/// <summary>
/// Reads active IPv4 TCP connections with their owning process, via the IP Helper API's
/// GetExtendedTcpTable - the same mechanism <c>netstat -ano</c> and Resource Monitor's
/// connection list use, and it needs no elevation (verified on this machine: unlike
/// <c>pktmon</c>, this call succeeds as a standard user). IPv6 and UDP are out of scope for
/// this first pass - TCP/IPv4 covers the large majority of a typical desktop's traffic
/// (web, most downloads) and keeps this read-only reader's scope bounded.
/// </summary>
public sealed class ProcessNetworkConnectionsReader : IProcessNetworkConnectionsReader
{
    private const int AfInet = 2;
    private const int TcpTableOwnerPidAll = 5;
    private const int ErrorInsufficientBuffer = 122;
    private const int RowSizeBytes = 24; // 6 DWORDs: state, localAddr, localPort, remoteAddr, remotePort, owningPid

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(IntPtr tcpTable, ref int size, bool sorted, int family, int tableClass, uint reserved);

    public Task<IReadOnlyList<ProcessNetworkConnection>> GetActiveConnectionsAsync(CancellationToken cancellationToken) =>
        Task.Run(() => ReadConnections(), cancellationToken);

    private static IReadOnlyList<ProcessNetworkConnection> ReadConnections()
    {
        int size = 0;
        _ = GetExtendedTcpTable(IntPtr.Zero, ref size, sorted: true, AfInet, TcpTableOwnerPidAll, 0);
        if (size <= 0)
            return Array.Empty<ProcessNetworkConnection>();

        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            var result = GetExtendedTcpTable(buffer, ref size, sorted: true, AfInet, TcpTableOwnerPidAll, 0);
            if (result != 0 && result != ErrorInsufficientBuffer)
                throw new InvalidOperationException($"GetExtendedTcpTable failed with error code {result}.");

            int rowCount = Marshal.ReadInt32(buffer);
            var connections = new List<ProcessNetworkConnection>(rowCount);
            var rowsStart = IntPtr.Add(buffer, 4);

            for (int i = 0; i < rowCount; i++)
            {
                var rowPtr = IntPtr.Add(rowsStart, i * RowSizeBytes);
                connections.Add(ReadRow(rowPtr));
            }

            return connections;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static ProcessNetworkConnection ReadRow(IntPtr rowPtr)
    {
        uint state = (uint)Marshal.ReadInt32(rowPtr, 0);
        uint localAddr = (uint)Marshal.ReadInt32(rowPtr, 4);
        uint localPortRaw = (uint)Marshal.ReadInt32(rowPtr, 8);
        uint remoteAddr = (uint)Marshal.ReadInt32(rowPtr, 12);
        uint remotePortRaw = (uint)Marshal.ReadInt32(rowPtr, 16);
        int owningPid = Marshal.ReadInt32(rowPtr, 20);

        return new ProcessNetworkConnection(
            ProcessId: owningPid,
            ProcessName: ResolveProcessName(owningPid),
            LocalAddress: new IPAddress(localAddr).ToString(),
            LocalPort: SwapPort(localPortRaw),
            RemoteAddress: new IPAddress(remoteAddr).ToString(),
            RemotePort: SwapPort(remotePortRaw),
            State: DescribeState(state));
    }

    // dwLocalPort/dwRemotePort store the port in the low 16 bits, in network (big-endian)
    // byte order - a raw cast would read e.g. port 443 as 0xBB01 instead of 0x01BB.
    private static int SwapPort(uint rawPort)
    {
        ushort masked = (ushort)(rawPort & 0xFFFF);
        return (masked & 0xFF) << 8 | (masked >> 8) & 0xFF;
    }

    private static string ResolveProcessName(int processId)
    {
        try
        {
            // The process can exit between enumeration and this lookup (a race inherent to
            // reading live OS state) - that is a normal, expected outcome here, not a defect.
            using var process = Process.GetProcessById(processId);
            return process.ProcessName;
        }
        catch (ArgumentException)
        {
            return processId == 0 ? "(sistema)" : $"(PID {processId}, proceso finalizado)";
        }
    }

    private static string DescribeState(uint state) => state switch
    {
        1 => "CLOSED",
        2 => "LISTEN",
        3 => "SYN_SENT",
        4 => "SYN_RCVD",
        5 => "ESTABLISHED",
        6 => "FIN_WAIT1",
        7 => "FIN_WAIT2",
        8 => "CLOSE_WAIT",
        9 => "CLOSING",
        10 => "LAST_ACK",
        11 => "TIME_WAIT",
        12 => "DELETE_TCB",
        _ => $"DESCONOCIDO ({state})",
    };
}
