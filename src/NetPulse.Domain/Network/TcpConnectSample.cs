namespace NetPulse.Domain.Network;

/// <summary>
/// A single TCP connection attempt - exists specifically to distinguish "ICMP is filtered"
/// from "this target is actually unreachable at the application level," which ICMP alone
/// cannot do (see LatencySample's own doc comment). One attempt per diagnostic run, not
/// repeated like ICMP sampling: a TCP handshake either reliably succeeds or reliably
/// doesn't on a healthy path, so repeating it adds traffic without adding confidence the
/// way repeated ICMP sampling does for jitter/loss statistics.
/// </summary>
public sealed record TcpConnectSample(
    string Target,
    int Port,
    DateTimeOffset Timestamp,
    bool Connected,
    TimeSpan? ConnectTime,
    string? Error);
