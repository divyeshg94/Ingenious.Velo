using System.Security.Cryptography;
using System.Text;

namespace Velo.Api.Services;

/// <summary>
/// Generates and validates the token used in re-engagement email unsubscribe links, so an
/// org's admin contact can opt out with a single unauthenticated click (no ADO login) while
/// a third party still can't opt an org out by guessing its OrgId.
/// </summary>
public interface IUnsubscribeTokenService
{
    /// <summary>True when a signing secret is configured — the campaign refuses to run without one.</summary>
    bool IsConfigured { get; }

    /// <summary>HMAC-SHA256 of the OrgId, URL-safe base64, truncated to 22 chars (~128 bits).</summary>
    string GenerateToken(string orgId);

    bool ValidateToken(string orgId, string token);
}

public class UnsubscribeTokenService(IConfiguration configuration) : IUnsubscribeTokenService
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(configuration["Marketing:UnsubscribeSecret"]);

    public string GenerateToken(string orgId)
    {
        var secret = configuration["Marketing:UnsubscribeSecret"]
            ?? throw new InvalidOperationException("Marketing:UnsubscribeSecret is not configured.");

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(orgId));
        return Convert.ToBase64String(hash)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    public bool ValidateToken(string orgId, string token)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(token)) return false;

        var expected = GenerateToken(orgId);
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var actualBytes = Encoding.UTF8.GetBytes(token);

        // Constant-time comparison — this token gates a state-changing action.
        return expectedBytes.Length == actualBytes.Length
            && CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }
}
