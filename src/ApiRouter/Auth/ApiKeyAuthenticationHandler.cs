using System.Security.Claims;
using System.Text.Encodings.Web;
using ApiRouter.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ApiRouter.Auth;

/// <summary>
/// Authenticates callers by the <c>X-Api-Key</c> request header, looking the key
/// up against active users. Keeps the API stateless: every request carries its own
/// credential and no server-side session is held.
/// </summary>
public class ApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "ApiKey";
    public const string HeaderName = "X-Api-Key";

    /// <summary>Role granted to admin users and required to manage users, targets, and rules.</summary>
    public const string AdminRole = "Admin";

    /// <summary>Authorization policy name backing <see cref="AdminRole"/>.</summary>
    public const string AdminPolicy = "Admin";

    private readonly RouterDbContext _db;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        RouterDbContext db)
        : base(options, logger, encoder)
    {
        _db = db;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderName, out var provided) || string.IsNullOrWhiteSpace(provided))
        {
            return AuthenticateResult.NoResult();
        }

        var apiKeyHash = ApiKeyHasher.Hash(provided.ToString());
        var user = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.ApiKeyHash == apiKeyHash, Context.RequestAborted);

        if (user is null || !user.IsActive)
        {
            return AuthenticateResult.Fail("Invalid or inactive API key.");
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Name),
        };
        if (user.IsAdmin)
        {
            claims.Add(new Claim(ClaimTypes.Role, AdminRole));
        }

        var identity = new ClaimsIdentity(claims, SchemeName, ClaimTypes.Name, ClaimTypes.Role);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return AuthenticateResult.Success(ticket);
    }
}
