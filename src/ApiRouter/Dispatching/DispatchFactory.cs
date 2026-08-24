using ApiRouter.Dtos;
using ApiRouter.Models;

namespace ApiRouter.Dispatching;

/// <summary>Builds a <see cref="Dispatch"/> entity from an inbound request DTO.</summary>
public static class DispatchFactory
{
    public static Dispatch Build(int userId, DispatchRequest request, int? scheduleId = null)
    {
        var dispatch = new Dispatch
        {
            UserId = userId,
            Mode = request.Mode,
            Status = DispatchStatus.Pending,
            ScheduleId = scheduleId,
        };

        var sequence = 0;
        foreach (var step in request.Steps)
        {
            dispatch.Steps.Add(new DispatchStep
            {
                Sequence = sequence++,
                TargetKey = step.TargetKey,
                Method = step.Method,
                Path = step.Path,
                Body = step.Body?.GetRawText(),
                Parameters = step.Parameters?.GetRawText(),
                Status = StepStatus.Pending,
            });
        }

        return dispatch;
    }
}
