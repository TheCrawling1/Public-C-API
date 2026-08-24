namespace ApiRouter.Models;

/// <summary>
/// A caller of the router. Every inbound request that runs a dispatch is
/// authenticated by the user's API key and evaluated against the rules that apply
/// to that user. Only the key's hash is stored — the raw key is shown once, at creation.
/// </summary>
public class User
{
    public int Id { get; set; }

    /// <summary>Human-friendly display name, e.g. "demo" or "nightly-job".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// SHA-256 hash of the key presented in the <c>X-Api-Key</c> header. The raw key
    /// is never persisted, so it cannot be recovered or leaked from storage.
    /// </summary>
    public string ApiKeyHash { get; set; } = string.Empty;

    /// <summary>Inactive users are rejected at authentication.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Admins may manage users, targets, and rules; regular users may only run dispatches.</summary>
    public bool IsAdmin { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Rule> Rules { get; set; } = new List<Rule>();
    public ICollection<Dispatch> Dispatches { get; set; } = new List<Dispatch>();
    public ICollection<Schedule> Schedules { get; set; } = new List<Schedule>();
}
