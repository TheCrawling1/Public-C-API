using ApiRouter.Auth;
using Xunit;

namespace ApiRouter.Tests;

public class ApiKeyHasherTests
{
    [Fact]
    public void Hash_Is_Deterministic()
    {
        Assert.Equal(ApiKeyHasher.Hash("abc"), ApiKeyHasher.Hash("abc"));
    }

    [Fact]
    public void Hash_Differs_By_Input()
    {
        Assert.NotEqual(ApiKeyHasher.Hash("abc"), ApiKeyHasher.Hash("abd"));
    }

    [Fact]
    public void Hash_Is_64_Hex_Chars_And_Not_The_Input()
    {
        var hash = ApiKeyHasher.Hash("demo-key-please-change");

        Assert.Equal(64, hash.Length);
        Assert.Matches("^[0-9a-f]{64}$", hash);
        Assert.NotEqual("demo-key-please-change", hash);
    }

    [Fact]
    public void GenerateKey_Produces_Unique_High_Entropy_Keys()
    {
        var a = ApiKeyHasher.GenerateKey();
        var b = ApiKeyHasher.GenerateKey();

        Assert.Equal(64, a.Length);
        Assert.Matches("^[0-9a-f]{64}$", a);
        Assert.NotEqual(a, b);
    }
}
