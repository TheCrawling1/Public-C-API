using ApiRouter.Models;

namespace ApiRouter.Dtos;

public record CreateScheduleRequest(
    string Name,
    int IntervalSeconds,
    DispatchRequest Dispatch,
    bool IsActive = true);

public record UpdateScheduleRequest(
    string? Name,
    int? IntervalSeconds,
    DispatchRequest? Dispatch,
    bool? IsActive);

public record ScheduleResponse(
    int Id,
    int UserId,
    string Name,
    int IntervalSeconds,
    bool IsActive,
    DateTime? LastRunAt,
    DateTime NextRunAt,
    DateTime CreatedAt)
{
    public static ScheduleResponse From(Schedule s) =>
        new(s.Id, s.UserId, s.Name, s.IntervalSeconds, s.IsActive,
            s.LastRunAt, s.NextRunAt, s.CreatedAt);
}
