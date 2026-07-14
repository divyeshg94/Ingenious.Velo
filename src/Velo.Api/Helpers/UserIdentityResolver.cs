using System.Security.Claims;

namespace Velo.Api.Helpers;

/// <summary>
/// Resolves stable user identity values from JWT claims emitted by Azure DevOps and Azure AD.
/// </summary>
public static class UserIdentityResolver
{
    public static string? ResolveUserIdentifier(ClaimsPrincipal? user)
        => ResolveFirstNonEmptyClaim(user,
            "emails",
            "email",
            "preferred_username",
            "upn",
            ClaimTypes.Email,
            ClaimTypes.Upn,
            ClaimTypes.NameIdentifier,
            "sub",
            "oid");

    public static string? ResolveDisplayName(ClaimsPrincipal? user)
        => ResolveFirstNonEmptyClaim(user,
            "name",
            ClaimTypes.Name,
            "given_name");

    private static string? ResolveFirstNonEmptyClaim(ClaimsPrincipal? user, params string[] claimTypes)
    {
        if (user is null)
            return null;

        foreach (var claimType in claimTypes)
        {
            var value = user.FindFirst(claimType)?.Value?.Trim();
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }
}
