using NetPulse.Domain.PrivilegedOperations;

namespace NetPulse.UnitTests.PrivilegedOperations;

public class DnsServerAddressValidatorTests
{
    [Fact]
    public void TryValidate_ValidIPv4Addresses_Succeeds()
    {
        var isValid = DnsServerAddressValidator.TryValidate(new[] { "1.1.1.1", "8.8.8.8" }, out var error);

        Assert.True(isValid);
        Assert.Empty(error);
    }

    [Fact]
    public void TryValidate_ValidIPv6Address_Succeeds()
    {
        var isValid = DnsServerAddressValidator.TryValidate(new[] { "2606:4700:4700::1111" }, out _);

        Assert.True(isValid);
    }

    [Fact]
    public void TryValidate_EmptyList_SucceedsAsAnAutomaticDnsRequest()
    {
        // Empty means "clear any static override and use DHCP-assigned DNS" - this is
        // exactly what rolling back an adapter that had no static DNS before needs to send.
        var isValid = DnsServerAddressValidator.TryValidate(Array.Empty<string>(), out var error);

        Assert.True(isValid);
        Assert.Empty(error);
    }

    [Fact]
    public void TryValidate_NullList_Fails()
    {
        var isValid = DnsServerAddressValidator.TryValidate(null, out var error);

        Assert.False(isValid);
    }

    [Fact]
    public void TryValidate_TooManyAddresses_Fails()
    {
        var isValid = DnsServerAddressValidator.TryValidate(
            new[] { "1.1.1.1", "8.8.8.8", "9.9.9.9", "76.76.2.0", "4.4.4.4" }, out var error);

        Assert.False(isValid);
        Assert.Contains("At most", error);
    }

    [Theory]
    [InlineData("not-an-ip")]
    [InlineData("1.1.1.1; rm -rf /")]
    [InlineData("dns.google")] // hostnames are never accepted, only literals
    [InlineData("999.999.999.999")]
    public void TryValidate_NonLiteralOrMalformedAddress_Fails(string address)
    {
        var isValid = DnsServerAddressValidator.TryValidate(new[] { address }, out var error);

        Assert.False(isValid);
        Assert.NotEmpty(error);
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("::")]
    public void TryValidate_UnusableAddress_Fails(string address)
    {
        var isValid = DnsServerAddressValidator.TryValidate(new[] { address }, out var error);

        Assert.False(isValid);
    }

    [Fact]
    public void TryValidate_OneValidAndOneInvalid_FailsOnTheInvalidOne()
    {
        var isValid = DnsServerAddressValidator.TryValidate(new[] { "1.1.1.1", "not-an-ip" }, out var error);

        Assert.False(isValid);
        Assert.Contains("not-an-ip", error);
    }
}
