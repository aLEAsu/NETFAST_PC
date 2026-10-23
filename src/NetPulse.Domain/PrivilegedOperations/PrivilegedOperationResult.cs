namespace NetPulse.Domain.PrivilegedOperations;

public sealed record PrivilegedOperationResult(
    PrivilegedCapability Capability,
    bool Succeeded,
    string? Output,
    string? Error,
    DateTimeOffset CompletedAt);
