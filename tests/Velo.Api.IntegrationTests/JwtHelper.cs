using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;

namespace Velo.Api.IntegrationTests;

/// <summary>
/// Generates unsigned JWTs that satisfy Velo.Api's lenient token validation
/// (ValidateIssuerSigningKey=false, RequireSignedTokens=false).
/// </summary>
public static class JwtHelper
{
    public static string CreateToken(
        string orgId = "test-org",
        string userId = "test-user",
        int expiryYears = 10)
    {
        var claims = new[]
        {
            new Claim("tid", orgId),
            new Claim("sub", userId),
            new Claim("oid", userId),
        };

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims, "TestScheme"),
            Expires = DateTime.UtcNow.AddYears(expiryYears),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(new byte[32]),
                SecurityAlgorithms.HmacSha256)
        };

        var handler = new JwtSecurityTokenHandler();
        return handler.CreateEncodedJwt(descriptor);
    }
}
