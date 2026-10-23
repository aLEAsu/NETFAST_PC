using NetPulse.Domain.Profiles;

namespace NetPulse.UnitTests.Profiles;

public class DiagnosticProfileCatalogTests
{
    [Theory]
    [InlineData(DiagnosticProfile.General)]
    [InlineData(DiagnosticProfile.Gaming)]
    [InlineData(DiagnosticProfile.Downloads)]
    [InlineData(DiagnosticProfile.VideoCalls)]
    [InlineData(DiagnosticProfile.Conservative)]
    [InlineData(DiagnosticProfile.Maintenance)]
    public void Get_EveryDefinedProfile_ReturnsSettingsWithoutThrowing(DiagnosticProfile profile)
    {
        var settings = DiagnosticProfileCatalog.Get(profile);

        Assert.Equal(profile, settings.Profile);
        Assert.False(string.IsNullOrWhiteSpace(settings.DisplayName));
        Assert.False(string.IsNullOrWhiteSpace(settings.Focus));
        Assert.True(settings.SampleCount > 0);
        Assert.True(settings.IntervalBetweenSamples > TimeSpan.Zero);
        Assert.True(settings.ProbeTimeout > TimeSpan.Zero);
    }

    [Fact]
    public void All_ContainsEveryEnumValueExactlyOnce()
    {
        var allValues = Enum.GetValues<DiagnosticProfile>();

        Assert.Equal(allValues.Length, DiagnosticProfileCatalog.All.Count);
        foreach (var value in allValues)
            Assert.Contains(value, DiagnosticProfileCatalog.All);
    }

    [Fact]
    public void Conservative_UsesFewerSamplesThanGeneral()
    {
        // The whole point of this profile - it must measurably reduce generated traffic,
        // not just claim to.
        var general = DiagnosticProfileCatalog.Get(DiagnosticProfile.General);
        var conservative = DiagnosticProfileCatalog.Get(DiagnosticProfile.Conservative);

        Assert.True(conservative.SampleCount < general.SampleCount);
    }

    [Fact]
    public void NoProfile_EverSpecifiesAnOptimizationActionOrChange()
    {
        // Structural guard: DiagnosticProfileSettings has no field that could reference an
        // applyable change - profiles only ever adjust measurement and ordering.
        var properties = typeof(DiagnosticProfileSettings).GetProperties().Select(p => p.Name);

        Assert.DoesNotContain("Action", properties);
        Assert.DoesNotContain("Change", properties);
    }
}
