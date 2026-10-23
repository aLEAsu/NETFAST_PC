using NetPulse.Domain.Network;

namespace NetPulse.UnitTests.Network;

public class LatencyStatisticsTests
{
    private static LatencySample Responded(double ms, DateTimeOffset timestamp) =>
        new("1.1.1.1", timestamp, Responded: true, RoundTripTime: TimeSpan.FromMilliseconds(ms));

    private static LatencySample Lost(DateTimeOffset timestamp) =>
        new("1.1.1.1", timestamp, Responded: false, RoundTripTime: null);

    [Fact]
    public void From_AllResponded_ComputesMinMedianMax()
    {
        var baseTime = DateTimeOffset.UtcNow;
        var samples = new[]
        {
            Responded(10, baseTime),
            Responded(20, baseTime.AddMilliseconds(200)),
            Responded(30, baseTime.AddMilliseconds(400)),
        };

        var stats = LatencyStatistics.From("1.1.1.1", samples);

        Assert.Equal(3, stats.SampleCount);
        Assert.Equal(3, stats.RespondedCount);
        Assert.Equal(0, stats.PacketLossPercent);
        Assert.Equal(TimeSpan.FromMilliseconds(10), stats.Min);
        Assert.Equal(TimeSpan.FromMilliseconds(20), stats.Median);
        Assert.Equal(TimeSpan.FromMilliseconds(30), stats.Max);
    }

    [Fact]
    public void From_SomeLost_ComputesPacketLossPercent()
    {
        var baseTime = DateTimeOffset.UtcNow;
        var samples = new[]
        {
            Responded(10, baseTime),
            Lost(baseTime.AddMilliseconds(200)),
            Responded(12, baseTime.AddMilliseconds(400)),
            Lost(baseTime.AddMilliseconds(600)),
        };

        var stats = LatencyStatistics.From("1.1.1.1", samples);

        Assert.Equal(4, stats.SampleCount);
        Assert.Equal(2, stats.RespondedCount);
        Assert.Equal(50.0, stats.PacketLossPercent);
    }

    [Fact]
    public void From_AllLost_ReturnsZeroedRttStatisticsWithFullLoss()
    {
        var baseTime = DateTimeOffset.UtcNow;
        var samples = new[] { Lost(baseTime), Lost(baseTime.AddMilliseconds(200)) };

        var stats = LatencyStatistics.From("1.1.1.1", samples);

        Assert.Equal(0, stats.RespondedCount);
        Assert.Equal(100.0, stats.PacketLossPercent);
        Assert.Equal(TimeSpan.Zero, stats.Median);
    }

    [Fact]
    public void From_StableLatency_ProducesLowJitter()
    {
        var baseTime = DateTimeOffset.UtcNow;
        var samples = Enumerable.Range(0, 10)
            .Select(i => Responded(20, baseTime.AddMilliseconds(i * 200)))
            .ToArray();

        var stats = LatencyStatistics.From("1.1.1.1", samples);

        Assert.Equal(TimeSpan.Zero, stats.JitterMeanAbsoluteDeviation);
    }

    [Fact]
    public void From_VaryingLatency_ProducesNonZeroJitter()
    {
        var baseTime = DateTimeOffset.UtcNow;
        double[] rtts = { 10, 50, 10, 50, 10, 50 };
        var samples = rtts
            .Select((rtt, i) => Responded(rtt, baseTime.AddMilliseconds(i * 200)))
            .ToArray();

        var stats = LatencyStatistics.From("1.1.1.1", samples);

        Assert.Equal(TimeSpan.FromMilliseconds(40), stats.JitterMeanAbsoluteDeviation);
    }

    [Fact]
    public void From_EmptySampleList_Throws()
    {
        Assert.Throws<ArgumentException>(() => LatencyStatistics.From("1.1.1.1", Array.Empty<LatencySample>()));
    }
}
