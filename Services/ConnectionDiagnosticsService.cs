using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using ElyProxy.Models;

namespace ElyProxy.Services;

public sealed class ConnectionDiagnosticsService
{
    private static readonly Uri ProbeUri = new("https://www.gstatic.com/generate_204");
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(8);

    private readonly IWindowsProxyService _windowsProxyService;

    public ConnectionDiagnosticsService(IWindowsProxyService windowsProxyService)
    {
        _windowsProxyService = windowsProxyService;
    }

    public async Task<ConnectionDiagnosticsResult> RunAsync(
        bool coreRunning,
        int socksPort,
        string mode,
        string pacUrl,
        IEnumerable<string> bypassRules,
        CancellationToken cancellationToken = default)
    {
        var checks = new List<ConnectionDiagnosticCheck>
        {
            new(
                "Ядро",
                coreRunning,
                coreRunning ? "процесс работает" : "процесс не запущен",
                coreRunning ? null : "Нажмите «Подключить»; если ядро завершается, проверьте наличие Xray/sing-box и журнал.")
        };

        bool localProxyAvailable = await IsTcpPortOpenAsync(socksPort, cancellationToken);
        checks.Add(new ConnectionDiagnosticCheck(
            "Локальный SOCKS5",
            localProxyAvailable,
            localProxyAvailable ? $"порт 127.0.0.1:{socksPort} отвечает" : $"порт 127.0.0.1:{socksPort} недоступен",
            localProxyAvailable ? null : $"Переподключитесь и проверьте, не занят ли порт 127.0.0.1:{socksPort} другим приложением."));

        WindowsProxySnapshot currentSettings = _windowsProxyService.CaptureCurrentSettings();
        bool integrationPassed;
        string integrationDetails;
        switch (mode)
        {
            case "Proxy Lite":
                integrationPassed = _windowsProxyService.IsOwnedByPac(currentSettings, [pacUrl]);
                integrationDetails = integrationPassed ? "Windows использует PAC ElyProxy" : "PAC ElyProxy не активен в настройках Windows";
                break;
            case "Proxy Pro":
                integrationPassed = _windowsProxyService.IsOwnedByManualProxy(currentSettings, socksPort, bypassRules);
                integrationDetails = integrationPassed ? "Windows проксирует HTTP/HTTPS/SOCKS через ElyProxy" : "параметры системного прокси не совпадают с ElyProxy";
                break;
            case "ElyTun":
                integrationPassed = coreRunning;
                integrationDetails = integrationPassed ? "TUN-ядро запущено" : "TUN-ядро не запущено";
                break;
            default:
                integrationPassed = true;
                integrationDetails = "системный прокси выключен; проверяется локальный SOCKS5";
                break;
        }

        string? integrationAction = integrationPassed ? null : mode switch
        {
            "Proxy Lite" => "Подключитесь и включите Proxy Lite; проверьте адрес PAC в Параметрах Windows → Сеть и Интернет → Прокси.",
            "Proxy Pro" => "Повторно включите Proxy Pro; проверьте, не изменены ли системные настройки прокси Windows.",
            "ElyTun" => "Проверьте выбранное ядро TUN и службу-помощник в настройках ElyTun, затем подключитесь снова.",
            _ => "Откройте настройки режима и включите нужный прокси после подключения."
        };
        checks.Add(new ConnectionDiagnosticCheck("Режим Windows", integrationPassed, integrationDetails, integrationAction));

        (bool routePassed, string routeDetails) = localProxyAvailable
            ? await ProbeRouteAsync(mode, socksPort, cancellationToken)
            : (false, "внешняя проверка пропущена: локальный порт недоступен");
        string? routeAction = routePassed
            ? null
            : localProxyAvailable
                ? "Проверьте состояние сервера, правила маршрутизации и DNS; тестовый HTTPS-сайт может быть недоступен отдельно."
                : "Сначала восстановите локальный порт и ядро, затем повторите проверку.";
        checks.Add(new ConnectionDiagnosticCheck("Внешний маршрут", routePassed, routeDetails, routeAction));
        return new ConnectionDiagnosticsResult(mode, checks);
    }

    private static async Task<bool> IsTcpPortOpenAsync(int port, CancellationToken cancellationToken)
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port, cancellationToken).AsTask().WaitAsync(ConnectTimeout, cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<(bool Passed, string Details)> ProbeRouteAsync(
        string mode,
        int socksPort,
        CancellationToken cancellationToken)
    {
        var handler = new SocketsHttpHandler { ConnectTimeout = ConnectTimeout };
        if (mode == "Proxy Pro")
        {
            handler.UseProxy = true;
            handler.Proxy = new WebProxy(new Uri($"http://127.0.0.1:{socksPort}"));
        }
        else if (mode is "Proxy Lite" or "SOCKS5")
        {
            handler.UseProxy = false;
            handler.ConnectCallback = (context, token) => ConnectThroughSocks5Async(context.DnsEndPoint, socksPort, token);
        }
        else
        {
            // In ElyTun, the operating system route sends this direct request through the TUN.
            handler.UseProxy = false;
        }

        using var client = new HttpClient(handler) { Timeout = RequestTimeout };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ElyProxy-Diagnostics");
        var stopwatch = Stopwatch.StartNew();
        try
        {
            if (mode == "ElyTun")
            {
                var result = await new ElyTunConnectivityService(client).CheckAsync(cancellationToken);
                return (result.Reachable, $"{result.Details}, {result.ElapsedMilliseconds} мс");
            }
            using HttpResponseMessage response = await client.GetAsync(ProbeUri, cancellationToken);
            stopwatch.Stop();
            int statusCode = (int)response.StatusCode;
            if (statusCode is >= 200 and < 400)
            {
                return (true, $"HTTP {statusCode}, {stopwatch.ElapsedMilliseconds} мс");
            }

            return (false, $"проверочный сервер вернул HTTP {statusCode}");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (false, "истекло время ожидания; проверьте сервер, маршрутизацию и DNS");
        }
        catch (HttpRequestException)
        {
            return (false, "не удалось завершить HTTPS-запрос через выбранный маршрут");
        }
        catch (Exception ex)
        {
            return (false, $"проверка завершилась ошибкой ({ex.GetType().Name})");
        }
    }

    private static async ValueTask<Stream> ConnectThroughSocks5Async(
        DnsEndPoint destination,
        int socksPort,
        CancellationToken cancellationToken)
    {
        var client = new TcpClient();
        try
        {
            await client.ConnectAsync(IPAddress.Loopback, socksPort, cancellationToken);
            NetworkStream stream = client.GetStream();
            await stream.WriteAsync(new byte[] { 5, 1, 0 }, cancellationToken);
            byte[] greeting = new byte[2];
            await stream.ReadExactlyAsync(greeting, cancellationToken);
            if (greeting[0] != 5 || greeting[1] != 0)
            {
                throw new IOException("SOCKS5 не принял метод без авторизации.");
            }

            byte[] request = CreateSocksConnectRequest(destination);
            await stream.WriteAsync(request, cancellationToken);
            byte[] response = new byte[4];
            await stream.ReadExactlyAsync(response, cancellationToken);
            if (response[0] != 5 || response[1] != 0)
            {
                throw new IOException("SOCKS5 отклонил подключение.");
            }

            int addressBytes = response[3] switch
            {
                1 => 4,
                4 => 16,
                3 => await ReadSocksDomainLengthAsync(stream, cancellationToken),
                _ => throw new IOException("SOCKS5 вернул неизвестный тип адреса.")
            };
            byte[] trailingBytes = new byte[addressBytes + 2];
            await stream.ReadExactlyAsync(trailingBytes, cancellationToken);
            return stream;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private static byte[] CreateSocksConnectRequest(DnsEndPoint destination)
    {
        var request = new List<byte> { 5, 1, 0 };
        if (IPAddress.TryParse(destination.Host, out IPAddress? address))
        {
            byte[] addressBytes = address.GetAddressBytes();
            request.Add(addressBytes.Length == 4 ? (byte)1 : (byte)4);
            request.AddRange(addressBytes);
        }
        else
        {
            byte[] hostBytes = Encoding.UTF8.GetBytes(destination.Host);
            if (hostBytes.Length > byte.MaxValue)
            {
                throw new IOException("Имя узла слишком длинное для SOCKS5.");
            }

            request.Add(3);
            request.Add((byte)hostBytes.Length);
            request.AddRange(hostBytes);
        }

        request.Add((byte)(destination.Port >> 8));
        request.Add((byte)destination.Port);
        return request.ToArray();
    }

    private static async Task<int> ReadSocksDomainLengthAsync(Stream stream, CancellationToken cancellationToken)
    {
        byte[] length = new byte[1];
        await stream.ReadExactlyAsync(length, cancellationToken);
        return length[0];
    }
}
