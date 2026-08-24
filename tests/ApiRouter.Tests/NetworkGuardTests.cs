using System.Net;
using ApiRouter.Actions;
using Xunit;

namespace ApiRouter.Tests;

public class NetworkGuardTests
{
    [Theory]
    // IPv4 private / special-use
    [InlineData("127.0.0.1")]
    [InlineData("10.1.2.3")]
    [InlineData("169.254.169.254")]   // cloud metadata
    [InlineData("192.168.0.10")]
    [InlineData("172.20.5.5")]
    [InlineData("100.100.100.200")]   // CGNAT / Alibaba metadata
    [InlineData("198.18.0.1")]        // benchmarking
    [InlineData("192.0.2.5")]         // TEST-NET-1
    [InlineData("203.0.113.7")]       // TEST-NET-3
    // IPv6 forms that embed an internal IPv4
    [InlineData("::ffff:169.254.169.254")] // IPv4-mapped
    [InlineData("64:ff9b::a9fe:a9fe")]     // NAT64 -> 169.254.169.254
    [InlineData("2002:7f00:1::")]          // 6to4 -> 127.0.0.1
    [InlineData("::7f00:1")]               // IPv4-compatible -> 127.0.0.1
    // IPv6 native private
    [InlineData("fd00::1")]                // unique-local
    [InlineData("fe80::1")]                // link-local
    public void Classifies_Internal_Addresses_As_Disallowed(string ip)
    {
        Assert.True(NetworkGuard.IsDisallowed(IPAddress.Parse(ip)));
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("2606:4700:4700::1111")]   // public IPv6
    [InlineData("2002:0808:0808::")]       // 6to4 embedding public 8.8.8.8
    public void Classifies_Public_Addresses_As_Allowed(string ip)
    {
        Assert.False(NetworkGuard.IsDisallowed(IPAddress.Parse(ip)));
    }

    // IP literals take the parse path (no DNS), so these are deterministic and offline.
    [Theory]
    [InlineData("http://127.0.0.1/x.jpg")]        // loopback
    [InlineData("http://10.0.0.5/x.jpg")]         // private
    [InlineData("http://172.16.4.4/x.jpg")]       // private
    [InlineData("http://192.168.1.1/x.jpg")]      // private
    [InlineData("http://169.254.169.254/x.jpg")]  // link-local / cloud metadata
    [InlineData("http://100.64.0.1/x.jpg")]       // CGNAT
    [InlineData("http://[::1]/x.jpg")]            // IPv6 loopback
    public async Task Blocks_Private_And_Local_Addresses(string url)
    {
        var (ok, reason) = await NetworkGuard.ValidatePublicHostAsync(new Uri(url), default);

        Assert.False(ok);
        Assert.NotNull(reason);
    }

    [Theory]
    [InlineData("http://8.8.8.8/x.jpg")]
    [InlineData("https://1.1.1.1/x.jpg")]
    public async Task Allows_Public_Addresses(string url)
    {
        var (ok, _) = await NetworkGuard.ValidatePublicHostAsync(new Uri(url), default);

        Assert.True(ok);
    }
}
