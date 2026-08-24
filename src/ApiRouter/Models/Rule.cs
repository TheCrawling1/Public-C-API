namespace ApiRouter.Models;

/// <summary>
/// A policy evaluated against every dispatch step. Rules are considered in
/// ascending <see cref="Priority"/> order and the first one that matches decides
/// the outcome. If no rule matches, the request is denied (secure by default).
/// </summary>
public class Rule
{
    public int Id { get; set; }

    /// <summary>
    /// The user this rule applies to. When <c>null</c> the rule is global and
    /// applies to every user.
    /// </summary>
    public int? UserId { get; set; }
    public User? User { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Allow or deny requests that match this rule.</summary>
    public RuleEffect Effect { get; set; } = RuleEffect.Deny;

    /// <summary>Lower numbers are evaluated first.</summary>
    public int Priority { get; set; } = 100;

    /// <summary>Restrict the rule to one target kind, or <c>null</c> for any.</summary>
    public TargetKind? TargetKind { get; set; }

    /// <summary>
    /// Glob pattern matched against a target's key (e.g. <c>"*"</c>, <c>"httpbin"</c>,
    /// <c>"internal-*"</c>). <c>null</c> or empty matches any target.
    /// </summary>
    public string? TargetPattern { get; set; }

    /// <summary>
    /// Glob pattern matched against the HTTP method for HTTP targets
    /// (e.g. <c>"GET"</c>, <c>"*"</c>). <c>null</c> or empty matches any method.
    /// </summary>
    public string? MethodPattern { get; set; }

    /// <summary>
    /// Optional sliding-window rate limit applied when this (allow) rule matches.
    /// <c>null</c> means unlimited.
    /// </summary>
    public int? MaxRequestsPerMinute { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
