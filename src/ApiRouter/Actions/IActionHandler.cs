using System.Text.Json;

namespace ApiRouter.Actions;

/// <summary>The result of running a local action handler.</summary>
public record ActionOutcome(bool Success, string Message)
{
    public static ActionOutcome Ok(string message) => new(true, message);
    public static ActionOutcome Fail(string message) => new(false, message);
}

/// <summary>
/// A local, in-process action a dispatch step can invoke instead of forwarding an
/// HTTP request. Implementations are discovered via DI and matched by <see cref="Name"/>,
/// so adding a new capability is just adding a class.
/// </summary>
public interface IActionHandler
{
    /// <summary>Matches <c>Target.ActionName</c>, e.g. "set-wallpaper".</summary>
    string Name { get; }

    Task<ActionOutcome> ExecuteAsync(JsonElement parameters, CancellationToken ct);
}
