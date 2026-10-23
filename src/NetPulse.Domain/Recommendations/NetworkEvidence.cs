using NetPulse.Domain.Diagnostics;
using NetPulse.Domain.Network;

namespace NetPulse.Domain.Recommendations;

/// <summary>
/// Everything one target's diagnostic run produced, bundled for rule evaluation. Grouping
/// it here (rather than widening <see cref="IRecommendationRule.Evaluate"/> to four separate
/// parameters) keeps the rule contract stable as more evidence types are added later - a
/// rule that only cares about latency can ignore <see cref="TcpCheck"/>/<see cref="DnsCheck"/>
/// without every call site needing to change again.
/// </summary>
public sealed record NetworkEvidence(
    LatencyStatistics Statistics,
    DiagnosticFinding Finding,
    TcpConnectSample TcpCheck,
    DnsResolutionSample DnsCheck);
