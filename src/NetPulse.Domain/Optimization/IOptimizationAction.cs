namespace NetPulse.Domain.Optimization;

/// <summary>
/// Contract every optimization must implement. There is deliberately no "run arbitrary
/// command" escape hatch: an action is a fixed, named piece of code reviewed ahead of time,
/// never a string assembled from user input. No implementation exists yet in this codebase -
/// Phase 3 (the current prototype) is diagnostics-only; this contract exists so the
/// recommendation engine and UI can be built against a stable shape before Phase 5 adds
/// real implementations behind NetPulse.PrivilegedAgent.
/// </summary>
public interface IOptimizationAction
{
    OptimizationDescriptor Describe();

    /// <summary>Verifies preconditions hold (OS version, adapter capability, driver, etc.).</summary>
    Task<bool> CheckAsync(CancellationToken cancellationToken);

    /// <summary>Captures current state so the change can be rolled back later.</summary>
    Task<string> CaptureAsync(CancellationToken cancellationToken);

    /// <summary>Applies the change. Must be idempotent-safe to call CheckAsync again after.</summary>
    Task<OptimizationResult> ApplyAsync(CancellationToken cancellationToken);

    /// <summary>Measures whether the change produced the declared, statistically meaningful effect.</summary>
    Task<OptimizationResult> VerifyAsync(CancellationToken cancellationToken);

    /// <summary>Restores the state captured by <see cref="CaptureAsync"/>.</summary>
    Task<OptimizationResult> RollbackAsync(string capturedState, CancellationToken cancellationToken);
}
