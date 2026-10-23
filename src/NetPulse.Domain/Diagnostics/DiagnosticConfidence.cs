namespace NetPulse.Domain.Diagnostics;

/// <summary>
/// Forces every finding surfaced to the user to declare how sure we actually are,
/// per the project principle that diagnosis must not be confused with optimization.
/// </summary>
public enum DiagnosticConfidence
{
    /// <summary>Reproducible, measured, and attributable to a specific cause.</summary>
    Confirmed,

    /// <summary>Evidence points this way but sample size or variability leaves room for doubt.</summary>
    Probable,

    /// <summary>Worth investigating further; not yet supported by enough evidence to act on.</summary>
    Hypothesis,

    /// <summary>Outside the PC's control (ISP, remote server, router, public internet path).</summary>
    ExternalLimitation,

    /// <summary>Checked and already within expected/healthy range - no action warranted.</summary>
    Healthy
}

public sealed record DiagnosticFinding(
    string Id,
    string Summary,
    DiagnosticConfidence Confidence,
    string Evidence,
    string[] Limitations);
