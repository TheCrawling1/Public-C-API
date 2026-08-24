namespace ApiRouter.Models;

/// <summary>
/// A destination the router can reach. Either an external HTTP API that requests
/// are forwarded to (<see cref="TargetKind.Http"/>), or a local action handler
/// executed in-process (<see cref="TargetKind.Action"/>, e.g. "set-wallpaper").
/// </summary>
public class Target
{
    public int Id { get; set; }

    /// <summary>Stable identifier referenced by dispatch steps, e.g. "httpbin".</summary>
    public string Key { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public TargetKind Kind { get; set; } = TargetKind.Http;

    /// <summary>Base URL for <see cref="TargetKind.Http"/> targets. A step's path is appended to it.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Handler name for <see cref="TargetKind.Action"/> targets, e.g. "set-wallpaper".</summary>
    public string? ActionName { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
