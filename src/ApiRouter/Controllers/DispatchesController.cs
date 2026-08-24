using ApiRouter.Auth;
using ApiRouter.Data;
using ApiRouter.Dispatching;
using ApiRouter.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ApiRouter.Controllers;

/// <summary>
/// The core endpoint. A caller submits one dispatch containing one or more
/// sub-requests (steps); the router authorizes each against the caller's rules,
/// forwards or executes them, and returns one bundled response.
/// </summary>
[ApiController]
[Route("api/dispatches")]
[Produces("application/json")]
[Authorize(AuthenticationSchemes = ApiKeyAuthenticationHandler.SchemeName)]
public class DispatchesController : ControllerBase
{
    /// <summary>Upper bound on sub-requests per dispatch, to cap outbound fan-out per call.</summary>
    public const int MaxStepsPerDispatch = 25;

    private const int MaxPageSize = 100;

    private readonly RouterDbContext _db;
    private readonly IDispatchExecutor _executor;

    public DispatchesController(RouterDbContext db, IDispatchExecutor executor)
    {
        _db = db;
        _executor = executor;
    }

    [HttpPost]
    public async Task<ActionResult<DispatchResponse>> Create(DispatchRequest request, CancellationToken ct)
    {
        var userId = User.GetUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        if (request.Steps is null || request.Steps.Count == 0)
        {
            return Problem(statusCode: 400, detail:"A dispatch must contain at least one step.");
        }

        if (request.Steps.Count > MaxStepsPerDispatch)
        {
            return Problem(statusCode: 400,
                detail: $"A dispatch may contain at most {MaxStepsPerDispatch} steps.");
        }

        if (request.Steps.Any(s => string.IsNullOrWhiteSpace(s.TargetKey)))
        {
            return Problem(statusCode: 400, detail:"Every step must specify a targetKey.");
        }

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId.Value, ct);
        if (user is null)
        {
            return Unauthorized();
        }

        var dispatch = DispatchFactory.Build(user.Id, request);
        _db.Dispatches.Add(dispatch);
        await _db.SaveChangesAsync(ct);

        await _executor.ExecuteAsync(dispatch, user, ct);

        return CreatedAtAction(nameof(GetById), new { id = dispatch.Id }, DispatchResponse.From(dispatch));
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<DispatchResponse>>> List(
        CancellationToken ct, [FromQuery] int skip = 0, [FromQuery] int take = 50)
    {
        var userId = User.GetUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        skip = Math.Max(0, skip);
        take = Math.Clamp(take, 1, MaxPageSize);

        var dispatches = await _db.Dispatches
            .AsNoTracking()
            .Include(d => d.Steps)
            .Where(d => d.UserId == userId.Value)
            .OrderByDescending(d => d.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

        return Ok(dispatches.Select(DispatchResponse.From));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<DispatchResponse>> GetById(int id, CancellationToken ct)
    {
        var userId = User.GetUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var dispatch = await _db.Dispatches
            .AsNoTracking()
            .Include(d => d.Steps)
            .FirstOrDefaultAsync(d => d.Id == id, ct);

        // Treat "not yours" the same as "not found" so ids can't be enumerated.
        if (dispatch is null || dispatch.UserId != userId.Value)
        {
            return NotFound();
        }

        return Ok(DispatchResponse.From(dispatch));
    }
}
