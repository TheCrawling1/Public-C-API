namespace ApiRouter.Models;

/// <summary>Whether a matching rule permits or blocks a request.</summary>
public enum RuleEffect
{
    Deny = 0,
    Allow = 1,
}

/// <summary>How a target is reached: a forwarded HTTP call, or a local action handler.</summary>
public enum TargetKind
{
    Http = 0,
    Action = 1,
}

/// <summary>Whether the steps of a dispatch run one-after-another or all at once.</summary>
public enum DispatchMode
{
    Sequential = 0,
    Parallel = 1,
}

/// <summary>Overall outcome of a dispatch job.</summary>
public enum DispatchStatus
{
    Pending = 0,
    Completed = 2,
    Failed = 3,
    Denied = 4,

    /// <summary>Some steps completed while others were denied or rate-limited (none failed).</summary>
    PartiallyCompleted = 5,
}

/// <summary>Outcome of a single step within a dispatch.</summary>
public enum StepStatus
{
    Pending = 0,
    Completed = 1,
    Failed = 2,
    Denied = 3,
    RateLimited = 4,
}
