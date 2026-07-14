using System.Security.Claims;
using FluentAssertions;
using Velo.Api.Helpers;

namespace Velo.Api.Tests.Helpers;

public class UserIdentityResolverTests
{
    [Fact]
    public void ResolveUserIdentifier_ReturnsEmail_WhenEmailClaimExists()
    {
        var principal = BuildPrincipal(
            new Claim("email", "dev@example.com"),
            new Claim("sub", "subject-123"));

        var result = UserIdentityResolver.ResolveUserIdentifier(principal);

        result.Should().Be("dev@example.com");
    }

    [Fact]
    public void ResolveUserIdentifier_FallsBackToSub_WhenEmailClaimsMissing()
    {
        var principal = BuildPrincipal(new Claim("sub", "subject-123"));

        var result = UserIdentityResolver.ResolveUserIdentifier(principal);

        result.Should().Be("subject-123");
    }

    [Fact]
    public void ResolveDisplayName_ReturnsName_WhenPresent()
    {
        var principal = BuildPrincipal(
            new Claim("name", "Dev User"),
            new Claim("given_name", "Dev"));

        var result = UserIdentityResolver.ResolveDisplayName(principal);

        result.Should().Be("Dev User");
    }

    [Fact]
    public void ResolveUserIdentifier_ReturnsNull_WhenNoKnownIdentityClaimExists()
    {
        var principal = BuildPrincipal(new Claim("tid", "tenant-only"));

        var result = UserIdentityResolver.ResolveUserIdentifier(principal);

        result.Should().BeNull();
    }

    private static ClaimsPrincipal BuildPrincipal(params Claim[] claims)
        => new(new ClaimsIdentity(claims, "test"));
}
