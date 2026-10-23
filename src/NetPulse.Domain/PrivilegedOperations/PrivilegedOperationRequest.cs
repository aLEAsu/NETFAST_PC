namespace NetPulse.Domain.PrivilegedOperations;

/// <summary>
/// A validated request to perform exactly one allow-listed capability. Every field is
/// already validated by the time this record exists (see <see cref="AdapterIdValidator"/>
/// and <see cref="DnsServerAddressValidator"/>) - this type carries no raw, unchecked user
/// input, and it is re-validated again at the elevated boundary itself (see
/// NetPulse.PrivilegedAgent.Host), which never trusts that a caller already did so.
/// </summary>
public sealed record PrivilegedOperationRequest(
    PrivilegedCapability Capability,
    string AdapterId,
    IReadOnlyList<string> Parameters,
    DateTimeOffset RequestedAt);
