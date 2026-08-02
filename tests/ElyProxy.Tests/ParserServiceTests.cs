using System.Text;
using ElyProxy.Services;

namespace ElyProxy.Tests;

public class ParserServiceTests
{
    private readonly ParserService _parser = new();

    [Fact]
    public void ParseVlessUri_ParsesIpv6AndTransportParameters()
    {
        const string uri = "vless://11111111-1111-1111-1111-111111111111@[2001:db8::10]:8443?type=ws&security=tls&sni=cdn.example.com&host=edge.example.com&path=%2Fws&alpn=h2%2Chttp%2F1.1#Test%20node";

        var node = _parser.ParseVlessUri(uri);

        Assert.NotNull(node);
        Assert.Equal("Test node", node.Name);
        Assert.Equal("2001:db8::10", node.Address);
        Assert.Equal(8443, node.Port);
        Assert.Equal("ws", node.Network);
        Assert.Equal("tls", node.Security);
        Assert.Equal("cdn.example.com", node.SNI);
        Assert.Equal("edge.example.com", node.Host);
        Assert.Equal("/ws", node.Path);
        Assert.Equal("h2,http/1.1", node.Alpn);
    }

    [Fact]
    public void ParseSubscriptionContent_DecodesUrlSafeBase64AndSkipsInvalidLines()
    {
        const string vless = "vless://22222222-2222-2222-2222-222222222222@example.com:443?type=grpc&security=reality&pbk=public-key&sid=abcd#Main";
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes($"ignored\n{vless}\nhttp://example.com"))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        var nodes = _parser.ParseSubscriptionContent(encoded);

        var node = Assert.Single(nodes);
        Assert.Equal("Main", node.Name);
        Assert.Equal("grpc", node.Network);
        Assert.Equal("reality", node.Security);
        Assert.Equal("public-key", node.PublicKey);
        Assert.Equal("abcd", node.ShortId);
    }

    [Theory]
    [InlineData("vless://uuid@example.com:0")]
    [InlineData("vless://uuid@example.com:65536")]
    [InlineData("vless://uuid@example.com:not-a-port")]
    [InlineData("vless://uuid@example.com")]
    public void ParseVlessUri_RejectsInvalidEndpoint(string uri)
    {
        Assert.Null(_parser.ParseVlessUri(uri));
    }
}
