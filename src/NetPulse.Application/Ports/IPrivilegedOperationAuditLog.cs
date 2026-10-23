namespace NetPulse.Application.Ports;

/// <summary>
/// Every step of every privileged operation attempt is recorded, successful or not - per
/// the brief's requirement that administrative operations be auditable. This is local-only
/// logging, never transmitted anywhere.
/// </summary>
public interface IPrivilegedOperationAuditLog
{
    Task RecordAsync(PrivilegedOperationAuditEntry entry, CancellationToken cancellationToken);
}

public sealed record PrivilegedOperationAuditEntry(
    DateTimeOffset Timestamp,
    string Capability,
    string AdapterId,
    string Phase,
    bool Succeeded,
    string Details);
