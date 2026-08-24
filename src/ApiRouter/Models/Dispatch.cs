namespace ApiRouter.Models;

/// <summary>
/// A single inbound job. A dispatch bundles one or more <see cref="DispatchStep"/>s
/// that the router validates against rules and then forwards or executes, returning
/// the aggregated results.
/// </summary>
public class Dispatch
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User? User { get; set; }

    public DispatchMode Mode { get; set; } = DispatchMode.Sequential;

    public DispatchStatus Status { get; set; } = DispatchStatus.Pending;

    /// <summary>Set when this dispatch was created by a schedule rather than a direct call.</summary>
    public int? ScheduleId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }

    public ICollection<DispatchStep> Steps { get; set; } = new List<DispatchStep>();
}
