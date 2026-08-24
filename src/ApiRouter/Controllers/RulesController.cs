using ApiRouter.Auth;
using ApiRouter.Data;
using ApiRouter.Dtos;
using ApiRouter.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ApiRouter.Controllers;

/// <summary>Manages the policy rules the engine evaluates on every dispatch step. Admin-only.</summary>
[ApiController]
[Route("api/rules")]
[Produces("application/json")]
[Authorize(AuthenticationSchemes = ApiKeyAuthenticationHandler.SchemeName, Policy = ApiKeyAuthenticationHandler.AdminPolicy)]
public class RulesController : ControllerBase
{
    private readonly RouterDbContext _db;

    public RulesController(RouterDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<RuleResponse>>> List(CancellationToken ct)
    {
        var rules = await _db.Rules.AsNoTracking()
            .OrderBy(r => r.Priority).ThenBy(r => r.Id)
            .ToListAsync(ct);
        return Ok(rules.Select(RuleResponse.From));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<RuleResponse>> GetById(int id, CancellationToken ct)
    {
        var rule = await _db.Rules.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
        return rule is null ? NotFound() : Ok(RuleResponse.From(rule));
    }

    [HttpPost]
    public async Task<ActionResult<RuleResponse>> Create(CreateRuleRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Problem(statusCode: 400, detail:"Name is required.");
        }

        if (request.UserId.HasValue && !await _db.Users.AnyAsync(u => u.Id == request.UserId.Value, ct))
        {
            return Problem(statusCode: 400, detail:$"User {request.UserId} does not exist.");
        }

        if (request.Priority < 0)
        {
            return Problem(statusCode: 400, detail: "Priority must be non-negative.");
        }

        if (request.MaxRequestsPerMinute is < 0)
        {
            return Problem(statusCode: 400, detail: "MaxRequestsPerMinute must be non-negative.");
        }

        var rule = new Rule
        {
            UserId = request.UserId,
            Name = request.Name.Trim(),
            Effect = request.Effect,
            Priority = request.Priority,
            TargetKind = request.TargetKind,
            TargetPattern = request.TargetPattern,
            MethodPattern = request.MethodPattern,
            MaxRequestsPerMinute = request.MaxRequestsPerMinute,
            IsActive = true,
        };
        _db.Rules.Add(rule);
        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetById), new { id = rule.Id }, RuleResponse.From(rule));
    }

    [HttpPatch("{id:int}")]
    public async Task<ActionResult<RuleResponse>> Update(int id, UpdateRuleRequest request, CancellationToken ct)
    {
        var rule = await _db.Rules.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (rule is null)
        {
            return NotFound();
        }

        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            rule.Name = request.Name.Trim();
        }

        if (request.Effect.HasValue)
        {
            rule.Effect = request.Effect.Value;
        }

        if (request.Priority.HasValue)
        {
            if (request.Priority.Value < 0)
            {
                return Problem(statusCode: 400, detail: "Priority must be non-negative.");
            }

            rule.Priority = request.Priority.Value;
        }

        if (request.TargetPattern is not null)
        {
            rule.TargetPattern = request.TargetPattern;
        }

        if (request.MethodPattern is not null)
        {
            rule.MethodPattern = request.MethodPattern;
        }

        if (request.MaxRequestsPerMinute.HasValue)
        {
            if (request.MaxRequestsPerMinute.Value < 0)
            {
                return Problem(statusCode: 400, detail: "MaxRequestsPerMinute must be non-negative.");
            }

            rule.MaxRequestsPerMinute = request.MaxRequestsPerMinute.Value;
        }

        if (request.IsActive.HasValue)
        {
            rule.IsActive = request.IsActive.Value;
        }

        await _db.SaveChangesAsync(ct);
        return Ok(RuleResponse.From(rule));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var rule = await _db.Rules.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (rule is null)
        {
            return NotFound();
        }

        _db.Rules.Remove(rule);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }
}
