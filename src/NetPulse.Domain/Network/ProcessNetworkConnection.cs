namespace NetPulse.Domain.Network;

/// <summary>
/// One active TCP connection and the process that owns it - the same correlation
/// <c>netstat -ano</c>/Resource Monitor's "Network" tab show, via the IP Helper API
/// (GetExtendedTcpTable), which needs no elevation. This is connection VISIBILITY, not a
/// bandwidth measurement: Module F (brief §6.2) explicitly requires distinguishing direct
/// measurements from estimates, and Windows has no unelevated, non-ETW API that reports real
/// per-process byte counts - see <see cref="NetPulse.Application.Ports.IProcessNetworkConnectionsReader"/>'s
/// remarks for why this project does not fabricate one.
/// </summary>
public sealed record ProcessNetworkConnection(
    int ProcessId,
    string ProcessName,
    string LocalAddress,
    int LocalPort,
    string RemoteAddress,
    int RemotePort,
    string State);
