namespace NetPulse.Domain.Profiles;

/// <summary>
/// The fixed, reviewed set of profiles this build ships with - same closed-catalog approach
/// as DefaultRecommendationRules, for the same reason: predictable, auditable behavior
/// rather than arbitrary configuration. Every profile here only adjusts measurement
/// parameters and result ordering; none of them changes what counts as healthy/unhealthy
/// (that stays in ConnectionDiagnosticUseCase.Classify and the recommendation rules,
/// unaffected by which profile is selected) and none applies a system change by itself.
///
/// Honesty note: "Gaming" and "VideoCalls" prioritize jitter/loss findings because those are
/// what this build can actually measure (ICMP + one TCP/DNS check) and that genuinely matter
/// for those use cases. Neither profile unlocks a throughput test, a stability-over-time
/// monitor, or anything else not implemented elsewhere in this codebase - a profile cannot
/// make an unimplemented capability appear.
/// </summary>
public static class DiagnosticProfileCatalog
{
    public static DiagnosticProfileSettings Get(DiagnosticProfile profile) => profile switch
    {
        DiagnosticProfile.General => new(
            DiagnosticProfile.General,
            "General",
            "Balanced defaults for everyday use - no particular finding is prioritized over another.",
            SampleCount: 20,
            IntervalBetweenSamples: TimeSpan.FromMilliseconds(200),
            ProbeTimeout: TimeSpan.FromSeconds(2),
            PriorityRecommendationIds: Array.Empty<string>()),

        DiagnosticProfile.Gaming => new(
            DiagnosticProfile.Gaming,
            "Gaming / low latency",
            "More samples to better detect jitter, since consistency matters more than raw throughput for real-time play. Jitter and latency findings are shown first.",
            SampleCount: 30,
            IntervalBetweenSamples: TimeSpan.FromMilliseconds(100),
            ProbeTimeout: TimeSpan.FromSeconds(1),
            PriorityRecommendationIds: new[] { "recommend.high-jitter-bufferbloat-followup", "recommend.high-median-latency" }),

        DiagnosticProfile.Downloads => new(
            DiagnosticProfile.Downloads,
            "Downloads / transfer performance",
            "Standard sampling - packet loss findings are shown first, since loss affects transfer throughput and retries more than jitter does.",
            SampleCount: 20,
            IntervalBetweenSamples: TimeSpan.FromMilliseconds(200),
            ProbeTimeout: TimeSpan.FromSeconds(2),
            PriorityRecommendationIds: new[] { "recommend.packet-loss-investigate" }),

        DiagnosticProfile.VideoCalls => new(
            DiagnosticProfile.VideoCalls,
            "Video calls / stability",
            "More samples, shorter spacing, to better catch the short jitter/loss spikes that cause call glitches. Both are shown first.",
            SampleCount: 30,
            IntervalBetweenSamples: TimeSpan.FromMilliseconds(150),
            ProbeTimeout: TimeSpan.FromSeconds(1),
            PriorityRecommendationIds: new[] { "recommend.high-jitter-bufferbloat-followup", "recommend.packet-loss-investigate" }),

        DiagnosticProfile.Conservative => new(
            DiagnosticProfile.Conservative,
            "Conservative",
            "Fewer samples and a longer spacing, to minimize the traffic this diagnostic itself generates. Results are less statistically confident as a direct trade-off.",
            SampleCount: 10,
            IntervalBetweenSamples: TimeSpan.FromMilliseconds(300),
            ProbeTimeout: TimeSpan.FromSeconds(2),
            PriorityRecommendationIds: Array.Empty<string>()),

        DiagnosticProfile.Maintenance => new(
            DiagnosticProfile.Maintenance,
            "Maintenance / thorough diagnosis",
            "More samples than General, for a more statistically confident one-off check - meant for deliberately investigating a problem, not for routine/frequent use.",
            SampleCount: 50,
            IntervalBetweenSamples: TimeSpan.FromMilliseconds(100),
            ProbeTimeout: TimeSpan.FromSeconds(2),
            PriorityRecommendationIds: Array.Empty<string>()),

        _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, "Unknown profile."),
    };

    public static IReadOnlyList<DiagnosticProfile> All { get; } = Enum.GetValues<DiagnosticProfile>();
}
