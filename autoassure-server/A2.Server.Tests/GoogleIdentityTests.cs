using A2.Server.Models;

namespace A2.Server.Tests;

/// <summary>Unit tests for <see cref="GoogleIdentity.IsEmailReallyVerified"/>.</summary>
public sealed class GoogleIdentityTests
{
    [Theory]
    [InlineData(true, "alice@gmail.com", null, true)] // gmail, verified
    [InlineData(true, "Alice@GMAIL.COM", null, true)] // gmail is case-insensitive
    [InlineData(true, "alice@acme.com", "acme.com", true)] // workspace domain, verified
    [InlineData(true, "alice@acme.com", null, false)] // non-gmail, no hosted domain: can't prove it
    [InlineData(false, "alice@gmail.com", null, false)] // Google says not verified, even though gmail
    [InlineData(false, "alice@acme.com", "acme.com", false)] // Google says not verified, even though hosted domain
    public void IsEmailReallyVerified_ReturnsExpected(
        bool emailVerified,
        string email,
        string? hostedDomain,
        bool expected
    )
    {
        var identity = new GoogleIdentity(
            "google-1",
            email,
            emailVerified,
            "Alice",
            "Anderson",
            hostedDomain
        );

        Assert.Equal(expected, identity.IsEmailReallyVerified());
    }
}
