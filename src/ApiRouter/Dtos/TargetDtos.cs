using ApiRouter.Models;

namespace ApiRouter.Dtos;

public record CreateTargetRequest(
    string Key,
    string Name,
    TargetKind Kind,
    string? BaseUrl,
    string? ActionName);

public record UpdateTargetRequest(
    string? Name,
    string? BaseUrl,
    string? ActionName,
    bool? IsActive);

public record TargetResponse(
    int Id,
    string Key,
    string Name,
    TargetKind Kind,
    string? BaseUrl,
    string? ActionName,
    bool IsActive,
    DateTime CreatedAt)
{
    public static TargetResponse From(Target t) =>
        new(t.Id, t.Key, t.Name, t.Kind, t.BaseUrl, t.ActionName, t.IsActive, t.CreatedAt);
}
