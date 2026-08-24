using System.Security.Claims;

namespace ApiRouter.Auth;

public static class ClaimsPrincipalExtensions
{
    /// <summary>Returns the authenticated user's id, or null if unauthenticated.</summary>
    public static int? GetUserId(this ClaimsPrincipal principal)
    {
        var raw = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(raw, out var id) ? id : null;
    }
}
