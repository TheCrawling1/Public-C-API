using ApiRouter.Models;

namespace ApiRouter.Dtos;

public record CreateRuleRequest(
    int? UserId,
    string Name,
    RuleEffect Effect,
    int Priority,
    TargetKind? TargetKind,
    string? TargetPattern,
    string? MethodPattern,
    int? MaxRequestsPerMinute);

public record UpdateRuleRequest(
    string? Name,
    RuleEffect? Effect,
    int? Priority,
    string? TargetPattern,
    string? MethodPattern,
    int? MaxRequestsPerMinute,
    bool? IsActive);

public record RuleResponse(
    int Id,
    int? UserId,
    string Name,
    RuleEffect Effect,
    int Priority,
    TargetKind? TargetKind,
    string? TargetPattern,
    string? MethodPattern,
    int? MaxRequestsPerMinute,
    bool IsActive,
    DateTime CreatedAt)
{
    public static RuleResponse From(Rule r) =>
        new(r.Id, r.UserId, r.Name, r.Effect, r.Priority, r.TargetKind,
            r.TargetPattern, r.MethodPattern, r.MaxRequestsPerMinute, r.IsActive, r.CreatedAt);
}
