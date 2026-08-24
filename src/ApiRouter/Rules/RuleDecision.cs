namespace ApiRouter.Rules;

/// <summary>The outcome of evaluating the rule set against one dispatch step.</summary>
public record RuleDecision(
    bool Allowed,
    string Reason,
    string? MatchedRule,
    int? RateLimitPerMinute)
{
    public static RuleDecision Allow(string rule, int? rateLimit) =>
        new(true, $"allowed by rule '{rule}'", rule, rateLimit);

    public static RuleDecision Deny(string reason, string? rule = null) =>
        new(false, reason, rule, null);
}
