using System.Text.Json;
using ApiRouter.Models;

namespace ApiRouter.Dtos;

/// <summary>The inbound job body: a mode plus one or more sub-requests (steps).</summary>
public record DispatchRequest(
    DispatchMode Mode,
    List<DispatchStepRequest> Steps);

/// <summary>One sub-request. HTTP fields apply to HTTP targets; Parameters to action targets.</summary>
public record DispatchStepRequest(
    string TargetKey,
    string? Method,
    string? Path,
    JsonElement? Body,
    JsonElement? Parameters);

public record DispatchResponse(
    int Id,
    int UserId,
    DispatchMode Mode,
    DispatchStatus Status,
    DateTime CreatedAt,
    DateTime? CompletedAt,
    List<DispatchStepResponse> Steps)
{
    public static DispatchResponse From(Dispatch d) =>
        new(d.Id, d.UserId, d.Mode, d.Status, d.CreatedAt, d.CompletedAt,
            d.Steps.OrderBy(s => s.Sequence).Select(DispatchStepResponse.From).ToList());
}

public record DispatchStepResponse(
    int Sequence,
    string TargetKey,
    string? Method,
    string? Path,
    StepStatus Status,
    int? ResponseStatusCode,
    string? ResponseBody,
    string? Error)
{
    public static DispatchStepResponse From(DispatchStep s) =>
        new(s.Sequence, s.TargetKey, s.Method, s.Path, s.Status,
            s.ResponseStatusCode, s.ResponseBody, s.Error);
}
