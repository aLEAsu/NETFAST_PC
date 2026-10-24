namespace NetPulse.Domain.Network;

/// <summary>
/// Grading thresholds chosen for this app, not a claim that they match any particular
/// third-party tool's scale - the brief requires technical honesty over borrowed authority,
/// so this is deliberately labeled as this project's own bucketing of "added median latency
/// under load" rather than attributed to an industry standard.
/// </summary>
public enum BufferbloatSeverity
{
    /// <summary>Added median latency under load is below 30 ms.</summary>
    Minimal,
    /// <summary>30-100 ms added.</summary>
    Moderate,
    /// <summary>100-300 ms added.</summary>
    Significant,
    /// <summary>300 ms or more added.</summary>
    Severe,
}

public sealed record BufferbloatSample(
    LatencyStatistics AtRest,
    LatencyStatistics UnderLoad,
    LoadDirection Direction,
    TimeSpan LoadDuration)
{
    public TimeSpan AddedMedianLatency => UnderLoad.Median - AtRest.Median;
    public TimeSpan AddedP95Latency => UnderLoad.P95 - AtRest.P95;

    public BufferbloatSeverity Severity => AddedMedianLatency.TotalMilliseconds switch
    {
        < 30 => BufferbloatSeverity.Minimal,
        < 100 => BufferbloatSeverity.Moderate,
        < 300 => BufferbloatSeverity.Significant,
        _ => BufferbloatSeverity.Severe,
    };
}
