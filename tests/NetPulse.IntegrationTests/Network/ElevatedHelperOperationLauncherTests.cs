using NetPulse.Domain.PrivilegedOperations;
using NetPulse.Infrastructure.Windows.Network;

namespace NetPulse.IntegrationTests.Network;

/// <summary>
/// Covers ONLY ElevatedHelperOperationLauncher's client-side validation, which runs and
/// returns a failure before the class ever touches the filesystem or starts a process - see
/// the class's own remarks on why its real launch path (ExecuteAsync succeeding past
/// validation) is never exercised by this automated session: that would attempt a real
/// "runas" elevation, which only a human may trigger. Lives in NetPulse.IntegrationTests
/// rather than NetPulse.UnitTests because the class under test is in
/// NetPulse.Infrastructure.Windows (net10.0-windows), which NetPulse.UnitTests (plain
/// net10.0) cannot reference.
/// </summary>
public class ElevatedHelperOperationLauncherTests
{
    private static readonly ElevatedHelperOperationLauncher Launcher = new();

    [Theory]
    [InlineData("0")]
    [InlineData("61")]
    [InlineData("not-a-number")]
    public async Task ExecuteAsync_CapturePacketCounters_InvalidDuration_FailsOnValidationAlone(string duration)
    {
        var request = new PrivilegedOperationRequest(
            PrivilegedCapability.CapturePacketCounters, AdapterId: string.Empty, Parameters: new[] { duration }, DateTimeOffset.UtcNow);

        var result = await Launcher.ExecuteAsync(request, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task ExecuteAsync_CapturePacketCounters_WrongParameterCount_Fails()
    {
        var request = new PrivilegedOperationRequest(
            PrivilegedCapability.CapturePacketCounters, AdapterId: string.Empty, Parameters: Array.Empty<string>(), DateTimeOffset.UtcNow);

        var result = await Launcher.ExecuteAsync(request, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("exactly one parameter", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_SetAdapterDnsServers_InvalidAdapterId_FailsOnValidationAlone()
    {
        var request = new PrivilegedOperationRequest(
            PrivilegedCapability.SetAdapterDnsServers, AdapterId: "not-a-guid", Parameters: new[] { "1.1.1.1" }, DateTimeOffset.UtcNow);

        var result = await Launcher.ExecuteAsync(request, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task ExecuteAsync_UnknownCapability_FailsWithoutTouchingAnything()
    {
        var request = new PrivilegedOperationRequest(
            (PrivilegedCapability)999, AdapterId: string.Empty, Parameters: Array.Empty<string>(), DateTimeOffset.UtcNow);

        var result = await Launcher.ExecuteAsync(request, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("no client-side validation", result.Error, StringComparison.OrdinalIgnoreCase);
    }
}
