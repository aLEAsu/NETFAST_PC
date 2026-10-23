using NetPulse.Infrastructure.Windows.Network;

namespace NetPulse.IntegrationTests.Network;

/// <summary>
/// Real DNS lookups - no fakes. "localhost" always resolves on a normal Windows machine
/// without touching the network, making it a deterministic success case; the RFC 2606
/// reserved ".invalid" TLD is guaranteed to never resolve, same pattern already used
/// elsewhere in this suite for DNS failure testing.
/// </summary>
public class DnsResolutionProbeServiceTests
{
    private static readonly DnsResolutionProbeService Service = new();

    [Fact]
    public async Task ProbeOnceAsync_Localhost_Resolves()
    {
        var sample = await Service.ProbeOnceAsync("localhost", TimeSpan.FromSeconds(2), CancellationToken.None);

        Assert.True(sample.Resolved);
        Assert.NotNull(sample.ResolutionTime);
        Assert.NotEmpty(sample.ResolvedAddresses);
        Assert.True(sample.IsApplicable);
    }

    [Fact]
    public async Task ProbeOnceAsync_ReservedInvalidTld_DoesNotResolve()
    {
        var sample = await Service.ProbeOnceAsync("this-host-does-not-exist.invalid", TimeSpan.FromSeconds(2), CancellationToken.None);

        Assert.False(sample.Resolved);
        Assert.NotNull(sample.Error);
    }

    [Fact]
    public async Task ProbeOnceAsync_CancelledToken_ThrowsOperationCancelled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Service.ProbeOnceAsync("localhost", TimeSpan.FromSeconds(2), cts.Token));
    }
}
