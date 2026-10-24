using NetPulse.Domain.Network;

namespace NetPulse.UnitTests.Network;

public class BufferbloatSampleTests
{
    private static LatencyStatistics StatsWithConstantRtt(double ms)
    {
        var baseTime = DateTimeOffset.UtcNow;
        var samples = new[]
        {
            new LatencySample("1.1.1.1", baseTime, Responded: true, RoundTripTime: TimeSpan.FromMilliseconds(ms)),
            new LatencySample("1.1.1.1", baseTime.AddMilliseconds(200), Responded: true, RoundTripTime: TimeSpan.FromMilliseconds(ms)),
            new LatencySample("1.1.1.1", baseTime.AddMilliseconds(400), Responded: true, RoundTripTime: TimeSpan.FromMilliseconds(ms)),
        };
        return LatencyStatistics.From("1.1.1.1", samples);
    }

    [Theory]
    [InlineData(10, 25, BufferbloatSeverity.Minimal)] // +15 ms
    [InlineData(10, 40, BufferbloatSeverity.Moderate)] // +30 ms
    [InlineData(10, 90, BufferbloatSeverity.Moderate)] // +80 ms
    [InlineData(10, 150, BufferbloatSeverity.Significant)] // +140 ms
    [InlineData(10, 400, BufferbloatSeverity.Severe)] // +390 ms
    public void Severity_GradesByAddedMedianLatency(double restMs, double loadedMs, BufferbloatSeverity expected)
    {
        var sample = new BufferbloatSample(
            StatsWithConstantRtt(restMs),
            StatsWithConstantRtt(loadedMs),
            LoadDirection.Download,
            TimeSpan.FromSeconds(10));

        Assert.Equal(expected, sample.Severity);
    }

    [Fact]
    public void AddedMedianLatency_IsTheDifferenceBetweenLoadedAndRestMedians()
    {
        var sample = new BufferbloatSample(
            StatsWithConstantRtt(20),
            StatsWithConstantRtt(150),
            LoadDirection.Upload,
            TimeSpan.FromSeconds(10));

        Assert.Equal(TimeSpan.FromMilliseconds(130), sample.AddedMedianLatency);
    }

    [Fact]
    public void AddedMedianLatency_CanBeNegative_WhenLoadedLatencyIsLowerThanRest()
    {
        // Real-world noise can make the "under load" sample look momentarily better than
        // the "at rest" one - this must not throw or clamp silently, since that would hide
        // a real (if small) measurement from the user.
        var sample = new BufferbloatSample(
            StatsWithConstantRtt(50),
            StatsWithConstantRtt(40),
            LoadDirection.Download,
            TimeSpan.FromSeconds(10));

        Assert.True(sample.AddedMedianLatency < TimeSpan.Zero);
        Assert.Equal(BufferbloatSeverity.Minimal, sample.Severity);
    }
}
