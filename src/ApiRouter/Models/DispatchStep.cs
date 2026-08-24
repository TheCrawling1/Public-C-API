namespace ApiRouter.Models;

/// <summary>
/// One sub-request inside a <see cref="Dispatch"/>, together with the result the
/// router recorded for it. This is the "request with sub-requests" idea: a caller
/// submits several steps in one dispatch and gets one bundled response back.
/// </summary>
public class DispatchStep
{
    public int Id { get; set; }

    public int DispatchId { get; set; }
    public Dispatch? Dispatch { get; set; }

    /// <summary>Execution order within the dispatch (0-based).</summary>
    public int Sequence { get; set; }

    /// <summary>Key of the <see cref="Target"/> this step is routed to.</summary>
    public string TargetKey { get; set; } = string.Empty;

    // ----- Request (input) -----

    /// <summary>HTTP method for HTTP targets, e.g. "GET" or "POST". Ignored for action targets.</summary>
    public string? Method { get; set; }

    /// <summary>Path appended to the target's base URL for HTTP targets, e.g. "/anything".</summary>
    public string? Path { get; set; }

    /// <summary>Raw JSON request body forwarded to HTTP targets.</summary>
    public string? Body { get; set; }

    /// <summary>Raw JSON parameters passed to an action handler, e.g. { "imageUrl": "..." }.</summary>
    public string? Parameters { get; set; }

    // ----- Result (output) -----

    public StepStatus Status { get; set; } = StepStatus.Pending;

    /// <summary>HTTP status code returned by an HTTP target, if any.</summary>
    public int? ResponseStatusCode { get; set; }

    /// <summary>Response body (HTTP) or handler message (action), truncated for storage.</summary>
    public string? ResponseBody { get; set; }

    /// <summary>Populated when the step was denied or failed.</summary>
    public string? Error { get; set; }
}
