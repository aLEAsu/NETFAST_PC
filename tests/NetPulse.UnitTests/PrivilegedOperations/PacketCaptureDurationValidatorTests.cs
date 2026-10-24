using NetPulse.Domain.PrivilegedOperations;

namespace NetPulse.UnitTests.PrivilegedOperations;

public class PacketCaptureDurationValidatorTests
{
    [Theory]
    [InlineData("1")]
    [InlineData("30")]
    [InlineData("60")]
    public void TryValidate_InRange_Succeeds(string durationText)
    {
        var isValid = PacketCaptureDurationValidator.TryValidate(durationText, out var seconds, out var error);

        Assert.True(isValid);
        Assert.Equal(int.Parse(durationText), seconds);
        Assert.Empty(error);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("61")]
    [InlineData("-5")]
    [InlineData("3600")]
    public void TryValidate_OutOfRange_Fails(string durationText)
    {
        var isValid = PacketCaptureDurationValidator.TryValidate(durationText, out _, out var error);

        Assert.False(isValid);
        Assert.NotEmpty(error);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("not-a-number")]
    [InlineData("5; pktmon unload")]
    public void TryValidate_NotANumber_Fails(string? durationText)
    {
        var isValid = PacketCaptureDurationValidator.TryValidate(durationText, out _, out var error);

        Assert.False(isValid);
        Assert.NotEmpty(error);
    }
}
