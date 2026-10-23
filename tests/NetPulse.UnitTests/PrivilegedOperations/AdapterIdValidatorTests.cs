using NetPulse.Domain.PrivilegedOperations;

namespace NetPulse.UnitTests.PrivilegedOperations;

public class AdapterIdValidatorTests
{
    [Theory]
    [InlineData("{20838DDC-A704-11F0-9799-806E6F6E6963}")]
    [InlineData("20838ddc-a704-11f0-9799-806e6f6e6963")]
    [InlineData("20838DDC-A704-11F0-9799-806E6F6E6963")]
    public void TryValidate_WellFormedGuid_WithOrWithoutBraces_Succeeds(string adapterId)
    {
        var isValid = AdapterIdValidator.TryValidate(adapterId, out var error);

        Assert.True(isValid);
        Assert.Empty(error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("not-a-guid")]
    [InlineData("'; DROP TABLE adapters; --")]
    [InlineData("{20838DDC-A704-11F0-9799-806E6F6E6963} && calc.exe")]
    public void TryValidate_InvalidInput_Fails(string? adapterId)
    {
        var isValid = AdapterIdValidator.TryValidate(adapterId, out var error);

        Assert.False(isValid);
        Assert.NotEmpty(error);
    }

    [Fact]
    public void Normalize_StripsBracesAndLowercases()
    {
        var normalized = AdapterIdValidator.Normalize("{20838DDC-A704-11F0-9799-806E6F6E6963}");

        Assert.Equal("20838ddc-a704-11f0-9799-806e6f6e6963", normalized);
    }

    [Fact]
    public void Normalize_BracedAndUnbracedForms_ProduceTheSameResult()
    {
        var withBraces = AdapterIdValidator.Normalize("{20838DDC-A704-11F0-9799-806E6F6E6963}");
        var withoutBraces = AdapterIdValidator.Normalize("20838ddc-a704-11f0-9799-806e6f6e6963");

        Assert.Equal(withBraces, withoutBraces);
    }
}
