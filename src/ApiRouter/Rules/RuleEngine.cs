using ApiRouter.Data;
using ApiRouter.Models;
using Microsoft.EntityFrameworkCore;

namespace ApiRouter.Rules;

public interface IRuleEngine
{
    /// <summary>
    /// Evaluates the active rules that apply to <paramref name="user"/> against a
    /// single step and returns the decision. Deny-by-default: if nothing matches,
    /// the step is rejected.
    /// </summary>
    Task<RuleDecision> EvaluateAsync(User user, Target target, string? method, CancellationToken ct);
}

public class RuleEngine : IRuleEngine
{
    private readonly RouterDbContext _db;

    public RuleEngine(RouterDbContext db) => _db = db;

    public async Task<RuleDecision> EvaluateAsync(User user, Target target, string? method, CancellationToken ct)
    {
        // Rules for this user plus global rules (UserId == null), lowest priority first.
        var rules = await _db.Rules
            .AsNoTracking()
            .Where(r => r.IsActive && (r.UserId == user.Id || r.UserId == null))
            .OrderBy(r => r.Priority)
            .ThenBy(r => r.Id)
            .ToListAsync(ct);

        foreach (var rule in rules)
        {
            if (!Matches(rule, target, method))
            {
                continue;
            }

            return rule.Effect == RuleEffect.Allow
                ? RuleDecision.Allow(rule.Name, rule.MaxRequestsPerMinute)
                : RuleDecision.Deny($"denied by rule '{rule.Name}'", rule.Name);
        }

        return RuleDecision.Deny("no matching rule (default deny)");
    }

    private static bool Matches(Rule rule, Target target, string? method)
    {
        if (rule.TargetKind is not null && rule.TargetKind != target.Kind)
        {
            return false;
        }

        if (!Glob.IsMatch(rule.TargetPattern, target.Key))
        {
            return false;
        }

        // Method only constrains HTTP targets; action targets have no method.
        if (target.Kind == TargetKind.Http && !Glob.IsMatch(rule.MethodPattern, method ?? "GET"))
        {
            return false;
        }

        return true;
    }
}
