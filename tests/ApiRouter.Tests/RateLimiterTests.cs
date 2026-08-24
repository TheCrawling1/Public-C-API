using ApiRouter.Rules;
using Xunit;

namespace ApiRouter.Tests;

public class RateLimiterTests
{
    [Fact]
    public void Allows_Up_To_The_Limit_Then_Blocks()
    {
        var limiter = new InMemoryRateLimiter();

        Assert.True(limiter.TryAcquire("user:target", 2));
        Assert.True(limiter.TryAcquire("user:target", 2));
        Assert.False(limiter.TryAcquire("user:target", 2));
    }

    [Fact]
    public void Tracks_Keys_Independently()
    {
        var limiter = new InMemoryRateLimiter();

        Assert.True(limiter.TryAcquire("a", 1));
        Assert.False(limiter.TryAcquire("a", 1));
        Assert.True(limiter.TryAcquire("b", 1));
    }

    [Fact]
    public void Zero_Or_Negative_Limit_Is_Unlimited()
    {
        var limiter = new InMemoryRateLimiter();

        Assert.True(limiter.TryAcquire("x", 0));
        Assert.True(limiter.TryAcquire("x", 0));
    }
}
