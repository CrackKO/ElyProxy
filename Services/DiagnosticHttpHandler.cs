using System.Diagnostics;
using System.Net.Http;
using System.Net.Sockets;

namespace ElyProxy.Services;

/// <summary>Records metadata only: never headers, URL paths, bodies or exception messages.</summary>
public sealed class DiagnosticHttpHandler : DelegatingHandler
{
    private readonly string _category;
    public DiagnosticHttpHandler(string category, HttpMessageHandler? inner = null)
        : base(inner ?? new HttpClientHandler()) => _category = category;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string id = Guid.NewGuid().ToString("N")[..12];
        var timer = Stopwatch.StartNew();
        LifecycleLog.Write("http.start", $"id={id} category={_category} method={request.Method} host={request.RequestUri?.IdnHost} scheme={request.RequestUri?.Scheme}");
        try
        {
            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            LifecycleLog.Write("http.response", $"id={id} status={(int)response.StatusCode} ms={timer.ElapsedMilliseconds} protocol={response.Version} length={response.Content.Headers.ContentLength}");
            return response;
        }
        catch (Exception ex)
        {
            LifecycleLog.Write("http.failed", $"id={id} ms={timer.ElapsedMilliseconds} cancelled={cancellationToken.IsCancellationRequested}");
            LogException("http.exception id=" + id, ex);
            NetworkDiagnosticSnapshot.Write("http.network id=" + id);
            throw;
        }
    }

    public static void LogException(string operation, Exception exception)
    {
        for (Exception? current = exception; current != null; current = current.InnerException)
        {
            string details = current is HttpRequestException http ? $" httpError={http.HttpRequestError} status={http.StatusCode}" :
                current is SocketException socket ? $" socket={socket.SocketErrorCode} native={socket.NativeErrorCode}" :
                current is System.ComponentModel.Win32Exception native ? $" native={native.NativeErrorCode:X8}" : "";
            LifecycleLog.Write(operation, $"type={current.GetType().FullName} hresult={current.HResult:X8}{details} stack={current.StackTrace}");
        }
    }
}
