namespace NetPulse.Domain.Network;

/// <summary>
/// A real, directly-measured throughput rate for one interface - Windows' own performance
/// counter (BytesSent/ReceivedPersec), not an estimate derived from anything else. This is
/// deliberately per-adapter, never attributed to a single process: there is no honest way to
/// split it among the processes active at the same moment without a real per-process byte
/// counter, which is exactly the gap <see cref="ProcessNetworkConnection"/>'s remarks explain.
/// Shown alongside the per-process connection list as context, not fused with it.
/// </summary>
public sealed record AdapterThroughputSample(
    string InterfaceName,
    double BytesSentPerSecond,
    double BytesReceivedPerSecond,
    DateTimeOffset SampledAt);
