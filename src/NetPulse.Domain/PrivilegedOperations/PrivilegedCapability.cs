namespace NetPulse.Domain.PrivilegedOperations;

/// <summary>
/// The complete, fixed set of elevated operations this application can ever perform. There
/// is deliberately no way to add a capability without adding a case here, recompiling, and
/// reviewing the code - per the brief's requirement that privileged operations use a fixed
/// allow-list rather than accept an executable name, arbitrary arguments, or a script from
/// the UI.
/// </summary>
public enum PrivilegedCapability
{
    /// <summary>Overwrite the DNS server search order configured on one adapter.</summary>
    SetAdapterDnsServers,

    /// <summary>
    /// Run a short, bounded pktmon counters-only session (packet/drop COUNTS per NIC -
    /// never packet content or payload) and return its raw text output. See
    /// NetPulse.PrivilegedAgent.PacketCounterCaptureAction for the full scope/privacy notes.
    /// </summary>
    CapturePacketCounters,
}
