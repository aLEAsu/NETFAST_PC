using NetPulse.Application.UseCases;

namespace NetPulse.UnitTests.UseCases;

public class DiagnosticOptionsTests
{
    [Fact]
    public void EffectiveInterval_NotSpecified_DefaultsTo200Milliseconds()
    {
        var options = new DiagnosticOptions(new[] { "1.1.1.1" });

        Assert.Equal(TimeSpan.FromMilliseconds(200), options.EffectiveInterval);
    }

    [Fact]
    public void EffectiveInterval_ExplicitlyZero_IsHonoredAsZero()
    {
        // Regression test: TimeSpan.Zero is bitwise identical to default(TimeSpan), so an
        // implementation that checks "== default" to decide whether a value was supplied
        // cannot distinguish "caller didn't set it" from "caller explicitly wants zero" -
        // it would silently substitute the 200 ms fallback instead. Found by a performance
        // test that measured actual elapsed time and got ~200x longer than expected.
        var options = new DiagnosticOptions(new[] { "1.1.1.1" }, IntervalBetweenSamples: TimeSpan.Zero);

        Assert.Equal(TimeSpan.Zero, options.EffectiveInterval);
    }

    [Fact]
    public void EffectiveTimeout_NotSpecified_DefaultsToTwoSeconds()
    {
        var options = new DiagnosticOptions(new[] { "1.1.1.1" });

        Assert.Equal(TimeSpan.FromSeconds(2), options.EffectiveTimeout);
    }

    [Fact]
    public void EffectiveTimeout_ExplicitlyZero_IsHonoredAsZero()
    {
        var options = new DiagnosticOptions(new[] { "1.1.1.1" }, ProbeTimeout: TimeSpan.Zero);

        Assert.Equal(TimeSpan.Zero, options.EffectiveTimeout);
    }

    [Fact]
    public void EffectiveInterval_ExplicitNonZeroValue_IsUsedAsGiven()
    {
        var options = new DiagnosticOptions(new[] { "1.1.1.1" }, IntervalBetweenSamples: TimeSpan.FromMilliseconds(50));

        Assert.Equal(TimeSpan.FromMilliseconds(50), options.EffectiveInterval);
    }
}
