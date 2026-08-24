using System.Text;
using System.Text.Json;
using ApiRouter.Actions;
using ApiRouter.Data;
using ApiRouter.Models;
using ApiRouter.Rules;
using Microsoft.EntityFrameworkCore;

namespace ApiRouter.Dispatching;

public interface IDispatchExecutor
{
    /// <summary>
    /// Runs every step of a dispatch: checks each against the rule engine and rate
    /// limiter, then forwards HTTP requests or invokes action handlers, recording
    /// each result. Persists the updated dispatch and steps before returning.
    /// </summary>
    Task ExecuteAsync(Dispatch dispatch, User user, CancellationToken ct);
}

public class DispatchExecutor : IDispatchExecutor
{
    private const int MaxStoredBodyLength = 10_000;

    private readonly RouterDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IRuleEngine _ruleEngine;
    private readonly IRateLimiter _rateLimiter;
    private readonly IEnumerable<IActionHandler> _actionHandlers;
    private readonly ILogger<DispatchExecutor> _logger;

    public DispatchExecutor(
        RouterDbContext db,
        IHttpClientFactory httpClientFactory,
        IRuleEngine ruleEngine,
        IRateLimiter rateLimiter,
        IEnumerable<IActionHandler> actionHandlers,
        ILogger<DispatchExecutor> logger)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _ruleEngine = ruleEngine;
        _rateLimiter = rateLimiter;
        _actionHandlers = actionHandlers;
        _logger = logger;
    }

    public async Task ExecuteAsync(Dispatch dispatch, User user, CancellationToken ct)
    {
        var keys = dispatch.Steps.Select(s => s.TargetKey).Distinct().ToList();
        var targets = await _db.Targets
            .AsNoTracking()
            .Where(t => keys.Contains(t.Key))
            .ToDictionaryAsync(t => t.Key, ct);

        // Phase 1: authorize every step up front (touches the DbContext, so it runs
        // sequentially even when the steps themselves will run in parallel).
        var ready = new List<(DispatchStep Step, Target Target)>();
        foreach (var step in dispatch.Steps.OrderBy(s => s.Sequence))
        {
            if (!targets.TryGetValue(step.TargetKey, out var target) || !target.IsActive)
            {
                step.Status = StepStatus.Failed;
                step.Error = $"Target '{step.TargetKey}' not found or inactive.";
                continue;
            }

            var decision = await _ruleEngine.EvaluateAsync(user, target, step.Method, ct);
            if (!decision.Allowed)
            {
                step.Status = StepStatus.Denied;
                step.Error = decision.Reason;
                continue;
            }

            if (decision.RateLimitPerMinute is int limit &&
                !_rateLimiter.TryAcquire($"{user.Id}:{target.Key}", limit))
            {
                step.Status = StepStatus.RateLimited;
                step.Error = $"Rate limit of {limit}/min exceeded for target '{target.Key}'.";
                continue;
            }

            ready.Add((step, target));
        }

        // Phase 2: perform the I/O. These tasks never touch the DbContext, so they
        // are safe to run concurrently; each only mutates its own step object.
        if (dispatch.Mode == DispatchMode.Parallel)
        {
            await Task.WhenAll(ready.Select(r => ExecuteStepAsync(r.Step, r.Target, ct)));
        }
        else
        {
            foreach (var (step, target) in ready)
            {
                await ExecuteStepAsync(step, target, ct);
            }
        }

        dispatch.Status = Summarize(dispatch.Steps);
        dispatch.CompletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    internal static DispatchStatus Summarize(IEnumerable<DispatchStep> steps)
    {
        var list = steps.ToList();
        if (list.Count == 0 || list.Any(s => s.Status == StepStatus.Failed))
        {
            return list.Count == 0 ? DispatchStatus.Completed : DispatchStatus.Failed;
        }

        if (list.All(s => s.Status == StepStatus.Completed))
        {
            return DispatchStatus.Completed;
        }

        if (list.All(s => s.Status is StepStatus.Denied or StepStatus.RateLimited))
        {
            return DispatchStatus.Denied;
        }

        // A mix of completed and denied/rate-limited steps (no failures).
        return DispatchStatus.PartiallyCompleted;
    }

    private Task ExecuteStepAsync(DispatchStep step, Target target, CancellationToken ct) =>
        target.Kind == TargetKind.Http
            ? ForwardHttpAsync(step, target, ct)
            : RunActionAsync(step, target, ct);

    private async Task ForwardHttpAsync(DispatchStep step, Target target, CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(target.BaseUrl))
            {
                step.Status = StepStatus.Failed;
                step.Error = $"HTTP target '{target.Key}' has no base URL.";
                return;
            }

            var client = _httpClientFactory.CreateClient("forward");
            var method = new HttpMethod(string.IsNullOrWhiteSpace(step.Method) ? "GET" : step.Method!);
            using var request = new HttpRequestMessage(method, CombineUrl(target.BaseUrl!, step.Path));

            if (!string.IsNullOrEmpty(step.Body))
            {
                request.Content = new StringContent(step.Body!, Encoding.UTF8, "application/json");
            }

            using var response = await client.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            step.ResponseStatusCode = (int)response.StatusCode;
            step.ResponseBody = Truncate(body);
            step.Status = response.IsSuccessStatusCode ? StepStatus.Completed : StepStatus.Failed;
            if (!response.IsSuccessStatusCode)
            {
                step.Error = $"Upstream returned status {(int)response.StatusCode}.";
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // A real cancellation aborts the dispatch; don't record it as a step failure.
            throw;
        }
        catch (Exception ex)
        {
            // Log the detail server-side; return a generic message so a failure can't be
            // used to probe internal network state.
            _logger.LogWarning(ex, "Forwarding step {Sequence} to {Target} failed", step.Sequence, target.Key);
            step.Status = StepStatus.Failed;
            step.Error = "The request to the target failed.";
        }
    }

    private async Task RunActionAsync(DispatchStep step, Target target, CancellationToken ct)
    {
        var handler = _actionHandlers.FirstOrDefault(h =>
            string.Equals(h.Name, target.ActionName, StringComparison.OrdinalIgnoreCase));

        if (handler is null)
        {
            step.Status = StepStatus.Failed;
            step.Error = $"No action handler registered for '{target.ActionName}'.";
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(step.Parameters) ? "{}" : step.Parameters!);
            var outcome = await handler.ExecuteAsync(doc.RootElement, ct);

            step.Status = outcome.Success ? StepStatus.Completed : StepStatus.Failed;
            step.ResponseBody = Truncate(outcome.Message);
            if (!outcome.Success)
            {
                step.Error = outcome.Message;
            }
        }
        catch (JsonException)
        {
            step.Status = StepStatus.Failed;
            step.Error = "Invalid JSON parameters.";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Detail (paths, stderr, exception text) stays in the log, not the response.
            _logger.LogWarning(ex, "Action step {Sequence} ({Action}) failed", step.Sequence, target.ActionName);
            step.Status = StepStatus.Failed;
            step.Error = "The action failed.";
        }
    }

    private static string CombineUrl(string baseUrl, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return baseUrl;
        }

        return $"{baseUrl.TrimEnd('/')}/{path.TrimStart('/')}";
    }

    private static string Truncate(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Length <= MaxStoredBodyLength
            ? value
            : value[..MaxStoredBodyLength] + "…(truncated)";
    }
}
