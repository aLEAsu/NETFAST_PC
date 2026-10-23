namespace NetPulse.Domain.Optimization;

public enum OptimizationRisk
{
    Low,
    Medium,
    High
}

public enum OptimizationOutcome
{
    NotAttempted,
    Applied,
    AppliedNotYetVerified,
    VerifiedImproved,
    VerifiedInconclusive,
    VerifiedRegressed,
    RolledBack,
    Failed
}

/// <summary>
/// Everything an action must declare before it is allowed to run, per the project's
/// requirement that no change be applied without a stated hypothesis, risk level, and
/// rollback path. This is metadata only - it carries no capability to execute anything.
/// </summary>
public sealed record OptimizationDescriptor(
    string Id,
    string Title,
    string Objective,
    string Evidence,
    string[] Preconditions,
    OptimizationRisk Risk,
    string[] RequiredPermissions,
    string VerificationMethod,
    string SuccessCriterion,
    string FailureCriterion,
    string RollbackMethod,
    string[] ReferenceDocumentation,
    string[] KnownLimitations);

public sealed record OptimizationResult(
    string ActionId,
    OptimizationOutcome Outcome,
    DateTimeOffset ExecutedAt,
    string? Details,
    string? Error);
