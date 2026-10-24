using NetPulse.Infrastructure.Windows.Network;

namespace NetPulse.IntegrationTests.Network;

/// <summary>Real WMI query against this machine's performance counters - read-only.</summary>
public class WmiAdapterThroughputReaderTests
{
    private static readonly WmiAdapterThroughputReader Reader = new();

    [Fact]
    public async Task GetCurrentThroughputAsync_OnARealMachine_ReturnsAtLeastOneInterface()
    {
        var samples = await Reader.GetCurrentThroughputAsync(CancellationToken.None);

        Assert.NotEmpty(samples);
    }

    [Fact]
    public async Task GetCurrentThroughputAsync_EverySample_HasANonEmptyNameAndNonNegativeRates()
    {
        var samples = await Reader.GetCurrentThroughputAsync(CancellationToken.None);

        Assert.All(samples, s =>
        {
            Assert.False(string.IsNullOrWhiteSpace(s.InterfaceName));
            Assert.True(s.BytesSentPerSecond >= 0);
            Assert.True(s.BytesReceivedPerSecond >= 0);
        });
    }

    [Fact]
    public async Task GetCurrentThroughputAsync_CalledTwiceInARow_DoesNotThrow()
    {
        var first = await Reader.GetCurrentThroughputAsync(CancellationToken.None);
        var second = await Reader.GetCurrentThroughputAsync(CancellationToken.None);

        Assert.NotNull(first);
        Assert.NotNull(second);
    }
}
