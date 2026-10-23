using NetPulse.Infrastructure.Windows.Network;

namespace NetPulse.IntegrationTests.Network;

/// <summary>
/// Real WMI query against this machine - read-only. There is no corresponding test for the
/// write path (SetDNSServerSearchOrder, reached only through
/// NetPulse.PrivilegedAgent.Host) in this suite: that write requires a real UAC-elevated
/// process and would actually change this machine's DNS configuration, which an automated
/// test run must never do. See documentation/02-roadmap-and-limitations.md for how that
/// path is meant to be verified instead (manually, deliberately, by a person).
/// </summary>
public class WmiAdapterDnsConfigurationReaderTests
{
    private static readonly WmiAdapterDnsConfigurationReader Reader = new();
    private static readonly NetworkAdapterInventoryService AdapterInventory = new();

    [Fact]
    public async Task GetDnsServersAsync_RealUpAdapter_DoesNotThrowAndReturnsAList()
    {
        var adapters = await AdapterInventory.GetAdaptersAsync(CancellationToken.None);
        var upAdapter = adapters.FirstOrDefault(a => a.IsUp && a.IPv4Addresses.Length > 0);

        // Not every test runner has a live, configured adapter (e.g. a locked-down CI image);
        // skip rather than fail when there is genuinely nothing to read.
        if (upAdapter is null)
            return;

        var servers = await Reader.GetDnsServersAsync(upAdapter.Id, CancellationToken.None);

        Assert.NotNull(servers);
    }

    [Fact]
    public async Task GetDnsServersAsync_UnknownAdapterId_ReturnsEmptyRatherThanThrowing()
    {
        var servers = await Reader.GetDnsServersAsync("{00000000-0000-0000-0000-000000000000}", CancellationToken.None);

        Assert.Empty(servers);
    }

    [Fact]
    public async Task GetDnsServersAsync_InvalidAdapterId_ThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => Reader.GetDnsServersAsync("not-a-guid", CancellationToken.None));
    }

    [Fact]
    public async Task GetDnsServersAsync_CalledTwiceInARow_ReturnsConsistentResults()
    {
        var adapters = await AdapterInventory.GetAdaptersAsync(CancellationToken.None);
        var upAdapter = adapters.FirstOrDefault(a => a.IsUp && a.IPv4Addresses.Length > 0);
        if (upAdapter is null)
            return;

        var first = await Reader.GetDnsServersAsync(upAdapter.Id, CancellationToken.None);
        var second = await Reader.GetDnsServersAsync(upAdapter.Id, CancellationToken.None);

        Assert.Equal(first, second);
    }
}
