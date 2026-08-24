using ApiRouter.Auth;
using ApiRouter.Data;
using ApiRouter.Dtos;
using ApiRouter.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ApiRouter.Controllers;

/// <summary>
/// Manages callers of the router. Admin-only: these endpoints expose API keys and
/// grant access, so they require an admin key (the seeded demo user is the bootstrap admin).
/// </summary>
[ApiController]
[Route("api/users")]
[Produces("application/json")]
[Authorize(AuthenticationSchemes = ApiKeyAuthenticationHandler.SchemeName, Policy = ApiKeyAuthenticationHandler.AdminPolicy)]
public class UsersController : ControllerBase
{
    private readonly RouterDbContext _db;

    public UsersController(RouterDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<UserResponse>>> List(CancellationToken ct)
    {
        var users = await _db.Users.AsNoTracking().OrderBy(u => u.Id).ToListAsync(ct);
        return Ok(users.Select(UserResponse.From));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<UserResponse>> GetById(int id, CancellationToken ct)
    {
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct);
        return user is null ? NotFound() : Ok(UserResponse.From(user));
    }

    [HttpPost]
    public async Task<ActionResult<UserCreatedResponse>> Create(CreateUserRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Problem(statusCode: 400, detail:"Name is required.");
        }

        // Generate the key, hand back the raw value once, and persist only its hash.
        var rawApiKey = ApiKeyHasher.GenerateKey();
        var user = new User
        {
            Name = request.Name.Trim(),
            ApiKeyHash = ApiKeyHasher.Hash(rawApiKey),
            IsActive = true,
            IsAdmin = request.IsAdmin,
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(
            nameof(GetById), new { id = user.Id }, UserCreatedResponse.From(user, rawApiKey));
    }

    [HttpPatch("{id:int}")]
    public async Task<ActionResult<UserResponse>> Update(int id, UpdateUserRequest request, CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null)
        {
            return NotFound();
        }

        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            user.Name = request.Name.Trim();
        }

        if (request.IsActive.HasValue)
        {
            user.IsActive = request.IsActive.Value;
        }

        if (request.IsAdmin.HasValue)
        {
            user.IsAdmin = request.IsAdmin.Value;
        }

        await _db.SaveChangesAsync(ct);
        return Ok(UserResponse.From(user));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null)
        {
            return NotFound();
        }

        _db.Users.Remove(user);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }
}
