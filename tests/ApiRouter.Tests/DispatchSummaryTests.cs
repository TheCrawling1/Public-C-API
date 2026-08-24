using ApiRouter.Dispatching;
using ApiRouter.Models;
using Xunit;

namespace ApiRouter.Tests;

public class DispatchSummaryTests
{
    private static List<DispatchStep> Steps(params StepStatus[] statuses)
    {
        var list = new List<DispatchStep>();
        var seq = 0;
        foreach (var s in statuses)
        {
            list.Add(new DispatchStep { Sequence = seq++, TargetKey = "t", Status = s });
        }

        return list;
    }

    [Fact]
    public void All_Completed_Is_Completed() =>
        Assert.Equal(DispatchStatus.Completed,
            DispatchExecutor.Summarize(Steps(StepStatus.Completed, StepStatus.Completed)));

    [Fact]
    public void Any_Failed_Is_Failed() =>
        Assert.Equal(DispatchStatus.Failed,
            DispatchExecutor.Summarize(Steps(StepStatus.Completed, StepStatus.Failed)));

    [Fact]
    public void All_Denied_Or_RateLimited_Is_Denied() =>
        Assert.Equal(DispatchStatus.Denied,
            DispatchExecutor.Summarize(Steps(StepStatus.Denied, StepStatus.RateLimited)));

    [Fact]
    public void Mixed_Completed_And_Denied_Is_PartiallyCompleted() =>
        Assert.Equal(DispatchStatus.PartiallyCompleted,
            DispatchExecutor.Summarize(Steps(StepStatus.Completed, StepStatus.Denied)));
}
