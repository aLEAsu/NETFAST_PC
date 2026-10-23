using NetPulse.Domain.Network;
using NetPulse.Infrastructure.Windows.Network;

namespace NetPulse.IntegrationTests.Network;

/// <summary>
/// Exercises the real Windows adapter inventory - no fakes. These tests depend on the host
/// machine actually having network adapters (true for any real Windows PC, including a
/// machine with no internet access: the loopback interface alone satisfies the assertions
/// below) and must stay strictly read-only, per the project's requirement that inventory
/// never modify machine state.
/// </summary>
public class NetworkAdapterInventoryServiceTests
{
    private static readonly NetworkAdapterInventoryService Service = new();

    [Fact]
    public async Task GetAdaptersAsync_OnARealMachine_ReturnsAtLeastOneAdapter()
    {
        var adapters = await Service.GetAdaptersAsync(CancellationToken.None);

        Assert.NotEmpty(adapters);
    }

    [Fact]
    public async Task GetAdaptersAsync_EveryAdapter_HasAStableNonEmptyIdAndName()
    {
        var adapters = await Service.GetAdaptersAsync(CancellationToken.None);

        Assert.All(adapters, a =>
        {
            Assert.False(string.IsNullOrWhiteSpace(a.Id));
            Assert.False(string.IsNullOrWhiteSpace(a.Name));
        });
    }

    [Fact]
    public async Task GetAdaptersAsync_IncludesTheLoopbackInterface()
    {
        // Every Windows machine exposes a loopback adapter regardless of physical
        // connectivity, so this assertion holds even on an isolated test runner.
        var adapters = await Service.GetAdaptersAsync(CancellationToken.None);

        Assert.Contains(adapters, a => a.Kind == AdapterKind.Loopback);
    }

    [Fact]
    public async Task GetAdaptersAsync_CalledTwiceInARow_DoesNotThrowAndIsConsistentInCount()
    {
        // Guards against the service leaking OS handles or throwing on repeated reads,
        // which would make it unsafe to call from a dashboard "refresh" button.
        var first = await Service.GetAdaptersAsync(CancellationToken.None);
        var second = await Service.GetAdaptersAsync(CancellationToken.None);

        Assert.Equal(first.Count, second.Count);
    }

    [Fact]
    public async Task GetAdaptersAsync_UpAdaptersWithIPv4_HaveAWellFormedAddress()
    {
        var adapters = await Service.GetAdaptersAsync(CancellationToken.None);

        var upWithIPv4 = adapters.Where(a => a.IsUp && a.IPv4Addresses.Length > 0);

        Assert.All(upWithIPv4, a =>
            Assert.All(a.IPv4Addresses, addr => Assert.True(System.Net.IPAddress.TryParse(addr, out _))));
    }

    [Fact]
    public async Task GetAdaptersAsync_NeverReturnsNegativeMtu()
    {
        // Mtu defaults to 0 when the OS does not expose it for a given interface type;
        // it must never come back negative, which would indicate a misread.
        var adapters = await Service.GetAdaptersAsync(CancellationToken.None);

        Assert.All(adapters, a => Assert.True(a.Mtu >= 0));
    }

    [Fact]
    public async Task GetAdaptersAsync_LoopbackIsFlaggedVirtual()
    {
        // Every Windows machine has this one, so it is a reliable, always-present anchor
        // for the IsVirtual classification - unlike WAN Miniports/6to4, whose exact count
        // varies by machine and by what networking features/VPN clients are installed.
        var adapters = await Service.GetAdaptersAsync(CancellationToken.None);

        Assert.All(adapters.Where(a => a.Kind == AdapterKind.Loopback), a => Assert.True(a.IsVirtual));
    }

    [Fact]
    public async Task GetAdaptersAsync_EveryTunnelKindAdapterIsFlaggedVirtual()
    {
        var adapters = await Service.GetAdaptersAsync(CancellationToken.None);

        Assert.All(adapters.Where(a => a.Kind == AdapterKind.Tunnel), a => Assert.True(a.IsVirtual));
    }

    [Fact]
    public async Task GetAdaptersAsync_WanMiniportAdaptersAreFlaggedVirtual()
    {
        // Windows' Remote Access service always installs several "WAN Miniport" pseudo-
        // adapters (one per VPN protocol it supports) whether or not the user has ever used
        // a VPN - these are exactly the entries the user's screenshot showed cluttering the
        // adapter list ("Conexión de área local* N"). This test only asserts something when
        // the host machine actually has them, since a stripped-down or non-Windows-standard
        // install might not.
        var adapters = await Service.GetAdaptersAsync(CancellationToken.None);

        var wanMiniports = adapters.Where(a => a.Description.Contains("WAN Miniport", StringComparison.OrdinalIgnoreCase));

        Assert.All(wanMiniports, a => Assert.True(a.IsVirtual));
    }

    [Fact]
    public async Task GetAdaptersAsync_AtLeastOneRealNonVirtualAdapterExists()
    {
        // Guards the opposite failure mode: a classifier so eager it flags everything as
        // virtual would silently empty the default (filtered) view in the dashboard, making
        // the app look broken even though adapters exist. Every machine this runs on has a
        // real physical or host-level adapter (Ethernet/Wi-Fi/USB Ethernet) besides loopback
        // and the virtual pseudo-adapters.
        var adapters = await Service.GetAdaptersAsync(CancellationToken.None);

        Assert.Contains(adapters, a => !a.IsVirtual);
    }
}
