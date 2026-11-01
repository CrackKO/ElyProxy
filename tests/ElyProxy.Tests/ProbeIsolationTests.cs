using ElyProxy.Core;
using ElyProxy.Models;
using ElyProxy.Services;
using Xunit;

namespace ElyProxy.Tests;

public class ProbeIsolationTests
{
    private static VlessNode Node(string network = "tcp") => new()
    {
        Protocol = "vless", Network = network, Address = "example.com", Port = 443,
        UUID = "11111111-1111-1111-1111-111111111111", Security = "tls", SNI = "example.com"
    };

    [Fact]
    public void LegacyProbeUsesAutoWithoutInheritingOffOrLiteXrayDefault()
    {
        Assert.Equal(ProxyCoreKind.SingBox, CoreSelectionPolicy.ResolveProbe(Node(), null, DpiSettings.Off, true, true).Core);
        Assert.Equal(ProxyCoreKind.Xray, CoreSelectionPolicy.ResolveProbe(Node("xhttp"), null, DpiSettings.Off, true, true).Core);
    }

    [Fact]
    public void ExplicitCoreAndDpiRequirementsAreRespected()
    {
        Assert.Equal(ProxyCoreKind.Xray, CoreSelectionPolicy.ResolveProbe(Node(), ProxyCoreKind.Xray, DpiSettings.Off, true, true).Core);
        var dpi = new DpiSettings(new(Fragment: true), new());
        Assert.Equal(ProxyCoreKind.Xray, CoreSelectionPolicy.ResolveProbe(Node(), null, dpi, true, true).Core);
        Assert.Throws<NotSupportedException>(() => CoreSelectionPolicy.ResolveProbe(Node(), ProxyCoreKind.SingBox, dpi, true, true));
    }

    [Fact]
    public async Task HttpDiagnosticsKeepStatusAndNeverLogUrlSecretsOrHeaders()
    {
        using var client = new HttpClient(new DiagnosticHttpHandler("test", new FakeHandler(false)));
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/private-test-token?secret=hidden-test-token");
        request.Headers.Add("Authorization", "Bearer header-test-secret");
        using var response = await client.SendAsync(request);
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, response.StatusCode);
        await LifecycleLog.FlushAsync();
        string log = File.ReadAllText(LifecycleLog.FilePath);
        Assert.Contains("status=403", log);
        Assert.DoesNotContain("private-test-token", log);
        Assert.DoesNotContain("hidden-test-token", log);
        Assert.DoesNotContain("header-test-secret", log);
    }

    [Fact]
    public async Task HttpDiagnosticsPreserveTlsExceptionAndHideItsSensitiveMessage()
    {
        using var client = new HttpClient(new DiagnosticHttpHandler("test", new FakeHandler(true)));
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("https://example.com/"));
        Assert.Equal(HttpRequestError.SecureConnectionError, error.HttpRequestError);
        await LifecycleLog.FlushAsync();
        string log = File.ReadAllText(LifecycleLog.FilePath);
        Assert.Contains("httpError=SecureConnectionError", log);
        Assert.Contains("System.Security.Authentication.AuthenticationException", log);
        Assert.DoesNotContain("sensitive-exception-token", log);
    }

    [Fact]
    public void CoreLogRedactsLinksAndCommonSecretFields()
    {
        string safe = LifecycleLog.Redact("hysteria2://user:secret@example.com/path token=hidden password:secret uuid=private-id");
        Assert.DoesNotContain("user:secret", safe);
        Assert.DoesNotContain("hidden", safe);
        Assert.DoesNotContain("private-id", safe);
        Assert.DoesNotContain("password:secret", safe);
    }

    private sealed class FakeHandler(bool fail) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (fail) throw new HttpRequestException(HttpRequestError.SecureConnectionError, "sensitive-exception-token",
                new System.Security.Authentication.AuthenticationException("sensitive-exception-token"));
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.Forbidden));
        }
    }
}
