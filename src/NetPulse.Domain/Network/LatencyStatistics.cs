namespace NetPulse.Domain.Network;

public sealed record LatencyStatistics(
    string Target,
    int SampleCount,
    int RespondedCount,
    double PacketLossPercent,
    TimeSpan Min,
    TimeSpan Median,
    TimeSpan P95,
    TimeSpan Max,
    TimeSpan JitterMeanAbsoluteDeviation,
    DateTimeOffset WindowStart,
    DateTimeOffset WindowEnd)
{
    /// <summary>
    /// Builds statistics from raw samples. Pure and deterministic so it can be unit-tested
    /// without any network access, per the project's requirement that measurement math be
    /// auditable independently of the Windows-specific probe that produced the samples.
    /// </summary>
    public static LatencyStatistics From(string target, IReadOnlyList<LatencySample> samples)
    {
        if (samples.Count == 0)
            throw new ArgumentException("At least one sample is required.", nameof(samples));

        var responded = samples.Where(s => s.Responded && s.RoundTripTime is not null).ToList();
        var rtts = responded.Select(s => s.RoundTripTime!.Value).OrderBy(t => t).ToList();

        double lossPercent = 100.0 * (samples.Count - responded.Count) / samples.Count;

        TimeSpan min = rtts.Count > 0 ? rtts[0] : TimeSpan.Zero;
        TimeSpan max = rtts.Count > 0 ? rtts[^1] : TimeSpan.Zero;
        TimeSpan median = rtts.Count > 0 ? Percentile(rtts, 0.50) : TimeSpan.Zero;
        TimeSpan p95 = rtts.Count > 0 ? Percentile(rtts, 0.95) : TimeSpan.Zero;
        TimeSpan jitter = ComputeJitter(responded);

        var timestamps = samples.Select(s => s.Timestamp).OrderBy(t => t).ToList();

        return new LatencyStatistics(
            Target: target,
            SampleCount: samples.Count,
            RespondedCount: responded.Count,
            PacketLossPercent: lossPercent,
            Min: min,
            Median: median,
            P95: p95,
            Max: max,
            JitterMeanAbsoluteDeviation: jitter,
            WindowStart: timestamps[0],
            WindowEnd: timestamps[^1]);
    }

    private static TimeSpan Percentile(List<TimeSpan> sortedAscending, double percentile)
    {
        if (sortedAscending.Count == 1)
            return sortedAscending[0];

        double rank = percentile * (sortedAscending.Count - 1);
        int lowerIndex = (int)Math.Floor(rank);
        int upperIndex = (int)Math.Ceiling(rank);
        double fraction = rank - lowerIndex;

        long lowerTicks = sortedAscending[lowerIndex].Ticks;
        long upperTicks = sortedAscending[upperIndex].Ticks;
        long interpolatedTicks = lowerTicks + (long)((upperTicks - lowerTicks) * fraction);
        return TimeSpan.FromTicks(interpolatedTicks);
    }

    /// <summary>
    /// Jitter here is the mean absolute difference between consecutive RTTs (RFC 3550-style),
    /// which is what bufferbloat/congestion analysis cares about - not the statistical
    /// standard deviation of the whole sample set.
    /// </summary>
    private static TimeSpan ComputeJitter(IReadOnlyList<LatencySample> respondedInOrder)
    {
        var ordered = respondedInOrder.OrderBy(s => s.Timestamp).Select(s => s.RoundTripTime!.Value).ToList();
        if (ordered.Count < 2)
            return TimeSpan.Zero;

        double sumAbsDeltaTicks = 0;
        for (int i = 1; i < ordered.Count; i++)
            sumAbsDeltaTicks += Math.Abs((ordered[i] - ordered[i - 1]).Ticks);

        return TimeSpan.FromTicks((long)(sumAbsDeltaTicks / (ordered.Count - 1)));
    }
}
