using NetPulse.Infrastructure.Windows.Network;

namespace NetPulse.IntegrationTests.Network;

/// <summary>
/// Exercises the real ICMP probe (no fake). Uses loopback for a guaranteed reply and a
/// reserved, non-routable TEST-NET-3 address (RFC 5737, 203.0.113.0/24) for a guaranteed
/// non-reply, so the suite is deterministic without depending on any third-party host being
/// reachable.
/// </summary>
public class IcmpLatencyProbeServiceTests
{
    private static readonly IcmpLatencyProbeService Service = new();

    [Fact]
    public async Task ProbeOnceAsync_Loopback_RespondsWithARoundTripTime()
    {
        var sample = await Service.ProbeOnceAsync("127.0.0.1", TimeSpan.FromSeconds(2), CancellationToken.None);

        Assert.True(sample.Responded);
        Assert.NotNull(sample.RoundTripTime);
        Assert.True(sample.RoundTripTime!.Value >= TimeSpan.Zero);
    }

    [Fact]
    public async Task ProbeOnceAsync_ReservedNonRoutableAddress_DoesNotRespond()
    {
        var sample = await Service.ProbeOnceAsync("203.0.113.1", TimeSpan.FromMilliseconds(800), CancellationToken.None);

        Assert.False(sample.Responded);
        Assert.Null(sample.RoundTripTime);
    }

    [Fact]
    public async Task ProbeOnceAsync_RecordsATimestampCloseToNow()
    {
        var before = DateTimeOffset.UtcNow;
        var sample = await Service.ProbeOnceAsync("127.0.0.1", TimeSpan.FromSeconds(2), CancellationToken.None);
        var after = DateTimeOffset.UtcNow;

        Assert.InRange(sample.Timestamp, before.AddSeconds(-1), after.AddSeconds(1));
    }

    [Fact]
    public async Task ProbeOnceAsync_UnresolvableHostname_DoesNotThrowAndIsReportedAsNoResponse()
    {
        // A probe service must never let a DNS failure surface as an unhandled exception -
        // it has to be reported as a sample like any other negative result.
        var sample = await Service.ProbeOnceAsync("this-host-does-not-exist.invalid", TimeSpan.FromSeconds(1), CancellationToken.None);

        Assert.False(sample.Responded);
    }

    [Fact]
    public async Task ProbeOnceAsync_RepeatedCalls_DoNotLeakResourcesOrThrow()
    {
        // Guards the "using var ping" lifecycle: ten sequential probes must behave
        // identically to one, which is what a real diagnostic run depends on.
        for (int i = 0; i < 10; i++)
        {
            var sample = await Service.ProbeOnceAsync("127.0.0.1", TimeSpan.FromSeconds(2), CancellationToken.None);
            Assert.True(sample.Responded);
        }
    }

    [Fact]
    public async Task ProbeOnceAsync_CancelledToken_ThrowsOperationCancelled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // The real exception is TaskCanceledException (thrown by Task.WaitAsync); any
        // OperationCanceledException satisfies the contract callers depend on.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Service.ProbeOnceAsync("127.0.0.1", TimeSpan.FromSeconds(2), cts.Token));
    }
}
