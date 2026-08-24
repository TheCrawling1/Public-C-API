using ApiRouter.Auth;
using ApiRouter.Data;
using ApiRouter.Dtos;
using ApiRouter.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ApiRouter.Controllers;

/// <summary>Manages the destinations the router may reach (HTTP APIs and local actions). Admin-only.</summary>
[ApiController]
[Route("api/targets")]
[Produces("application/json")]
[Authorize(AuthenticationSchemes = ApiKeyAuthenticationHandler.SchemeName, Policy = ApiKeyAuthenticationHandler.AdminPolicy)]
public class TargetsController : ControllerBase
{
    private readonly RouterDbContext _db;

    public TargetsController(RouterDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<TargetResponse>>> List(CancellationToken ct)
    {
        var targets = await _db.Targets.AsNoTracking().OrderBy(t => t.Id).ToListAsync(ct);
        return Ok(targets.Select(TargetResponse.From));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<TargetResponse>> GetById(int id, CancellationToken ct)
    {
        var target = await _db.Targets.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);
        return target is null ? NotFound() : Ok(TargetResponse.From(target));
    }

    [HttpPost]
    public async Task<ActionResult<TargetResponse>> Create(CreateTargetRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Key) || string.IsNullOrWhiteSpace(request.Name))
        {
            return Problem(statusCode: 400, detail:"Key and Name are required.");
        }

        if (request.Kind == TargetKind.Http &&
            (string.IsNullOrWhiteSpace(request.BaseUrl) ||
             !Uri.TryCreate(request.BaseUrl, UriKind.Absolute, out var baseUri) ||
             (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps)))
        {
            return Problem(statusCode: 400, detail: "HTTP targets require a valid absolute http(s) BaseUrl.");
        }

        if (request.Kind == TargetKind.Action && string.IsNullOrWhiteSpace(request.ActionName))
        {
            return Problem(statusCode: 400, detail:"Action targets require an ActionName.");
        }

        var key = request.Key.Trim();
        if (await _db.Targets.AnyAsync(t => t.Key == key, ct))
        {
            return Problem(statusCode: 409, detail: $"A target with key '{key}' already exists.");
        }

        var target = new Target
        {
            Key = key,
            Name = request.Name.Trim(),
            Kind = request.Kind,
            BaseUrl = request.BaseUrl,
            ActionName = request.ActionName,
            IsActive = true,
        };
        _db.Targets.Add(target);
        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetById), new { id = target.Id }, TargetResponse.From(target));
    }

    [HttpPatch("{id:int}")]
    public async Task<ActionResult<TargetResponse>> Update(int id, UpdateTargetRequest request, CancellationToken ct)
    {
        var target = await _db.Targets.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (target is null)
        {
            return NotFound();
        }

        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            target.Name = request.Name.Trim();
        }

        if (request.BaseUrl is not null)
        {
            if (request.BaseUrl.Length > 0 &&
                (!Uri.TryCreate(request.BaseUrl, UriKind.Absolute, out var baseUri) ||
                 (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps)))
            {
                return Problem(statusCode: 400, detail: "BaseUrl must be a valid absolute http(s) URL.");
            }

            target.BaseUrl = request.BaseUrl;
        }

        if (request.ActionName is not null)
        {
            target.ActionName = request.ActionName;
        }

        if (request.IsActive.HasValue)
        {
            target.IsActive = request.IsActive.Value;
        }

        await _db.SaveChangesAsync(ct);
        return Ok(TargetResponse.From(target));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var target = await _db.Targets.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (target is null)
        {
            return NotFound();
        }

        _db.Targets.Remove(target);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }
}
