using System.Text.Json;
using ApiRouter.Data;
using ApiRouter.Dispatching;
using ApiRouter.Dtos;
using ApiRouter.Serialization;
using Microsoft.EntityFrameworkCore;

namespace ApiRouter.Scheduling;

/// <summary>
/// Polls the schedule table on a fixed cadence and fires any dispatch whose next
/// run time has arrived. This is what turns a stored dispatch into an "automatic
/// request" — e.g. rotating the wallpaper every morning with no client involved.
/// </summary>
public class SchedulerHostedService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SchedulerHostedService> _logger;

    public SchedulerHostedService(IServiceScopeFactory scopeFactory, ILogger<SchedulerHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Scheduler started; polling every {Seconds}s.", PollInterval.TotalSeconds);

        using var timer = new PeriodicTimer(PollInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await FireDueSchedulesAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Scheduler tick failed.");
            }
        }
    }

    private async Task FireDueSchedulesAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RouterDbContext>();
        var executor = scope.ServiceProvider.GetRequiredService<IDispatchExecutor>();

        var now = DateTime.UtcNow;
        var due = await db.Schedules
            .Where(s => s.IsActive && s.NextRunAt <= now)
            .OrderBy(s => s.NextRunAt)
            .ToListAsync(ct);

        foreach (var schedule in due)
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == schedule.UserId, ct);
            if (user is null || !user.IsActive)
            {
                _logger.LogWarning("Schedule {Id} skipped: owner missing or inactive.", schedule.Id);
                schedule.NextRunAt = now.AddSeconds(Math.Max(5, schedule.IntervalSeconds));
                await db.SaveChangesAsync(ct);
                continue;
            }

            try
            {
                var request = JsonSerializer.Deserialize<DispatchRequest>(schedule.DispatchTemplate, JsonDefaults.Options);
                if (request is null || request.Steps is null || request.Steps.Count == 0)
                {
                    _logger.LogWarning("Schedule {Id} has an empty dispatch template.", schedule.Id);
                }
                else
                {
                    var dispatch = DispatchFactory.Build(user.Id, request, schedule.Id);
                    db.Dispatches.Add(dispatch);
                    await db.SaveChangesAsync(ct);
                    await executor.ExecuteAsync(dispatch, user, ct);
                    _logger.LogInformation(
                        "Schedule {Id} fired dispatch {DispatchId} ({Status}).",
                        schedule.Id, dispatch.Id, dispatch.Status);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Schedule {Id} failed to fire.", schedule.Id);
            }

            schedule.LastRunAt = now;
            schedule.NextRunAt = now.AddSeconds(Math.Max(5, schedule.IntervalSeconds));
            await db.SaveChangesAsync(ct);
        }
    }
}
