using ElyProxy.Services;
using Xunit;

namespace ElyProxy.Tests;

public class AuthenticatedProxyImportTests
{
    [Theory]
    [InlineData("socks5", "socks", "none")]
    [InlineData("socks", "socks", "none")]
    [InlineData("http", "http", "none")]
    [InlineData("https", "http", "tls")]
    public void ImportsProviderLinkAndEncodedCredentials(string scheme, string protocol, string security)
    {
        var parser = new ParserService();
        var link = $"{scheme}://account%3Aname:p%40ss%3Aword%23%25@192.0.2.25:8000#My%20proxy";
        var node = Assert.Single(parser.ParseSubscriptionContent(link));
        Assert.Equal(protocol, node.Protocol);
        Assert.Equal(security, node.Security);
        Assert.Equal("account:name", node.Username);
        Assert.Equal("p@ss:word#%", node.Password);
        Assert.Equal("192.0.2.25", node.Address);
        Assert.Equal(8000, node.Port);
        Assert.Equal("My proxy", node.Name);
    }

    [Theory]
    [InlineData("socks5")]
    [InlineData("http")]
    [InlineData("https")]
    public void ImportsIpv6WithoutAuthentication(string scheme)
    {
        var node = new ParserService().ParseUri($"{scheme}://[2001:db8::1]:8080");
        Assert.NotNull(node);
        Assert.Equal("2001:db8::1", node.Address);
        Assert.Equal(string.Empty, node.Username);
        Assert.Equal(string.Empty, node.Password);
    }

    [Theory]
    [InlineData("socks5://user:pass@192.0.2.25:0")]
    [InlineData("socks5://user:pass@192.0.2.25:65536")]
    [InlineData("socks5://user:pass@192.0.2.25:abc")]
    public void RejectsInvalidPort(string link) => Assert.Null(new ParserService().ParseUri(link));
}
