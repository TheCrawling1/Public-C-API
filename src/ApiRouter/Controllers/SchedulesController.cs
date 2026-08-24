using System.Text.Json;
using ApiRouter.Auth;
using ApiRouter.Data;
using ApiRouter.Dtos;
using ApiRouter.Models;
using ApiRouter.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ApiRouter.Controllers;

/// <summary>
/// Manages stored dispatches that the router fires automatically on an interval.
/// Scoped to the authenticated caller.
/// </summary>
[ApiController]
[Route("api/schedules")]
[Produces("application/json")]
[Authorize(AuthenticationSchemes = ApiKeyAuthenticationHandler.SchemeName)]
public class SchedulesController : ControllerBase
{
    private const int MaxSchedulesPerUser = 50;

    private readonly RouterDbContext _db;

    public SchedulesController(RouterDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ScheduleResponse>>> List(CancellationToken ct)
    {
        var userId = User.GetUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var schedules = await _db.Schedules
            .AsNoTracking()
            .Where(s => s.UserId == userId.Value)
            .OrderBy(s => s.Id)
            .ToListAsync(ct);

        return Ok(schedules.Select(ScheduleResponse.From));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ScheduleResponse>> GetById(int id, CancellationToken ct)
    {
        var owned = await FindOwnedAsync(id, ct);
        return owned is null ? NotFound() : Ok(ScheduleResponse.From(owned));
    }

    [HttpPost]
    public async Task<ActionResult<ScheduleResponse>> Create(CreateScheduleRequest request, CancellationToken ct)
    {
        var userId = User.GetUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Problem(statusCode: 400, detail:"Name is required.");
        }

        if (request.IntervalSeconds < 5)
        {
            return Problem(statusCode: 400, detail:"IntervalSeconds must be at least 5.");
        }

        if (request.Dispatch is null || request.Dispatch.Steps is null || request.Dispatch.Steps.Count == 0)
        {
            return Problem(statusCode: 400, detail:"A schedule must contain a dispatch with at least one step.");
        }

        if (request.Dispatch.Steps.Count > DispatchesController.MaxStepsPerDispatch)
        {
            return Problem(statusCode: 400,
                detail: $"A schedule's dispatch may contain at most {DispatchesController.MaxStepsPerDispatch} steps.");
        }

        if (await _db.Schedules.CountAsync(s => s.UserId == userId.Value, ct) >= MaxSchedulesPerUser)
        {
            return Problem(statusCode: 409,
                detail: $"A user may have at most {MaxSchedulesPerUser} schedules.");
        }

        var schedule = new Schedule
        {
            UserId = userId.Value,
            Name = request.Name.Trim(),
            IntervalSeconds = request.IntervalSeconds,
            DispatchTemplate = JsonSerializer.Serialize(request.Dispatch, JsonDefaults.Options),
            IsActive = request.IsActive,
            NextRunAt = DateTime.UtcNow,
        };
        _db.Schedules.Add(schedule);
        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetById), new { id = schedule.Id }, ScheduleResponse.From(schedule));
    }

    [HttpPatch("{id:int}")]
    public async Task<ActionResult<ScheduleResponse>> Update(int id, UpdateScheduleRequest request, CancellationToken ct)
    {
        var schedule = await FindOwnedAsync(id, ct);
        if (schedule is null)
        {
            return NotFound();
        }

        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            schedule.Name = request.Name.Trim();
        }

        if (request.IntervalSeconds.HasValue)
        {
            if (request.IntervalSeconds.Value < 5)
            {
                return Problem(statusCode: 400, detail:"IntervalSeconds must be at least 5.");
            }

            schedule.IntervalSeconds = request.IntervalSeconds.Value;
        }

        if (request.Dispatch is not null)
        {
            if (request.Dispatch.Steps is null || request.Dispatch.Steps.Count == 0)
            {
                return Problem(statusCode: 400, detail:"Dispatch must contain at least one step.");
            }

            if (request.Dispatch.Steps.Count > DispatchesController.MaxStepsPerDispatch)
            {
                return Problem(statusCode: 400,
                    detail: $"A schedule's dispatch may contain at most {DispatchesController.MaxStepsPerDispatch} steps.");
            }

            schedule.DispatchTemplate = JsonSerializer.Serialize(request.Dispatch, JsonDefaults.Options);
        }

        if (request.IsActive.HasValue)
        {
            schedule.IsActive = request.IsActive.Value;
        }

        await _db.SaveChangesAsync(ct);
        return Ok(ScheduleResponse.From(schedule));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var schedule = await FindOwnedAsync(id, ct);
        if (schedule is null)
        {
            return NotFound();
        }

        _db.Schedules.Remove(schedule);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task<Schedule?> FindOwnedAsync(int id, CancellationToken ct)
    {
        var userId = User.GetUserId();
        if (userId is null)
        {
            return null;
        }

        return await _db.Schedules.FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId.Value, ct);
    }
}
