namespace ApiRouter.Models;

/// <summary>
/// A stored dispatch that the router fires automatically on a fixed interval.
/// This is what makes "automatic requests" work — e.g. rotate the desktop
/// wallpaper every morning without anyone calling the API.
/// </summary>
public class Schedule
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User? User { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>How often the dispatch fires, in seconds.</summary>
    public int IntervalSeconds { get; set; } = 3600;

    /// <summary>
    /// The dispatch request to submit on each fire, stored as JSON
    /// (a serialized <c>DispatchRequest</c>).
    /// </summary>
    public string DispatchTemplate { get; set; } = "{}";

    public bool IsActive { get; set; } = true;

    public DateTime? LastRunAt { get; set; }
    public DateTime NextRunAt { get; set; } = DateTime.UtcNow;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
