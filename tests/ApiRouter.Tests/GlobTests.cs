using ApiRouter.Rules;
using Xunit;

namespace ApiRouter.Tests;

public class GlobTests
{
    [Theory]
    [InlineData(null, "anything")]
    [InlineData("", "anything")]
    [InlineData("*", "anything")]
    [InlineData("httpbin", "httpbin")]
    [InlineData("HTTPBIN", "httpbin")]      // case-insensitive
    [InlineData("internal-*", "internal-billing")]
    [InlineData("GET", "get")]
    [InlineData("h?t", "hat")]              // ? matches exactly one char
    [InlineData("v?", "v2")]
    public void Matches(string? pattern, string value)
    {
        Assert.True(Glob.IsMatch(pattern, value));
    }

    [Theory]
    [InlineData("httpbin", "other")]
    [InlineData("internal-*", "public-api")]
    [InlineData("GET", "POST")]
    [InlineData("h?t", "heat")]             // ? is exactly one char, not two
    public void DoesNotMatch(string pattern, string value)
    {
        Assert.False(Glob.IsMatch(pattern, value));
    }
}
