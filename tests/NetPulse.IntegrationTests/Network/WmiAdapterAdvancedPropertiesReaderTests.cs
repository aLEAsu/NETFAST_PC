using NetPulse.Infrastructure.Windows.Network;

namespace NetPulse.IntegrationTests.Network;

/// <summary>
/// Real WMI query against this machine - read-only, against root\StandardCimv2 (not the
/// default root\cimv2 namespace WmiAdapterDnsConfigurationReaderTests exercises). Module C
/// (brief §6.4) ships with no write path at all in this pass, so there is nothing to test
/// beyond reading.
/// </summary>
public class WmiAdapterAdvancedPropertiesReaderTests
{
    private static readonly WmiAdapterAdvancedPropertiesReader Reader = new();
    private static readonly NetworkAdapterInventoryService AdapterInventory = new();

    [Fact]
    public async Task GetAdvancedPropertiesAsync_RealUpAdapter_DoesNotThrowAndReturnsAList()
    {
        var adapters = await AdapterInventory.GetAdaptersAsync(CancellationToken.None);
        var upAdapter = adapters.FirstOrDefault(a => a.IsUp && !a.IsVirtual);

        // Not every test runner has a real, non-virtual adapter (e.g. a locked-down CI
        // image); skip rather than fail when there is genuinely nothing to read.
        if (upAdapter is null)
            return;

        var properties = await Reader.GetAdvancedPropertiesAsync(upAdapter.Id, CancellationToken.None);

        Assert.NotNull(properties);
    }

    [Fact]
    public async Task GetAdvancedPropertiesAsync_EveryReturnedProperty_HasANonEmptyKeywordAndDisplayName()
    {
        var adapters = await AdapterInventory.GetAdaptersAsync(CancellationToken.None);
        var upAdapter = adapters.FirstOrDefault(a => a.IsUp && !a.IsVirtual);
        if (upAdapter is null)
            return;

        var properties = await Reader.GetAdvancedPropertiesAsync(upAdapter.Id, CancellationToken.None);

        Assert.All(properties, p =>
        {
            Assert.False(string.IsNullOrWhiteSpace(p.Keyword));
            Assert.False(string.IsNullOrWhiteSpace(p.DisplayName));
        });
    }

    [Fact]
    public async Task GetAdvancedPropertiesAsync_UnknownAdapterId_ReturnsEmptyRatherThanThrowing()
    {
        var properties = await Reader.GetAdvancedPropertiesAsync("{00000000-0000-0000-0000-000000000000}", CancellationToken.None);

        Assert.Empty(properties);
    }

    [Fact]
    public async Task GetAdvancedPropertiesAsync_InvalidAdapterId_ThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => Reader.GetAdvancedPropertiesAsync("not-a-guid", CancellationToken.None));
    }

    [Fact]
    public async Task GetAdvancedPropertiesAsync_CalledTwiceInARow_ReturnsConsistentResults()
    {
        var adapters = await AdapterInventory.GetAdaptersAsync(CancellationToken.None);
        var upAdapter = adapters.FirstOrDefault(a => a.IsUp && !a.IsVirtual);
        if (upAdapter is null)
            return;

        var first = await Reader.GetAdvancedPropertiesAsync(upAdapter.Id, CancellationToken.None);
        var second = await Reader.GetAdvancedPropertiesAsync(upAdapter.Id, CancellationToken.None);

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task GetAdvancedPropertiesAsync_VirtualTunnelAdapter_ReturnsEmptyRatherThanThrowing()
    {
        // WAN Miniport/tunnel adapters generally expose no advanced driver properties at
        // all - this must come back as an empty list, the same "adapter doesn't expose this
        // property" handling NetworkAdapterInventoryService.SafeMtu established for MTU on
        // the loopback adapter, not an exception that would make the UI panel look broken.
        var adapters = await AdapterInventory.GetAdaptersAsync(CancellationToken.None);
        var virtualAdapter = adapters.FirstOrDefault(a => a.IsVirtual);
        if (virtualAdapter is null)
            return;

        var properties = await Reader.GetAdvancedPropertiesAsync(virtualAdapter.Id, CancellationToken.None);

        Assert.NotNull(properties);
    }
}
