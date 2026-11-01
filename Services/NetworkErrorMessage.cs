using System.Net.Http;
using System.Net.Sockets;
using System.Security.Authentication;

namespace ElyProxy.Services;

public static class NetworkErrorMessage
{
    public static string Describe(Exception exception)
    {
        DiagnosticHttpHandler.LogException("network.error", exception);
        if (exception is HttpRequestException { StatusCode: { } status }) return $"Сервер ответил HTTP {(int)status}.";
        var error = exception as HttpRequestException;
        if (error?.HttpRequestError == HttpRequestError.NameResolutionError || exception.GetBaseException() is SocketException { SocketErrorCode: SocketError.HostNotFound or SocketError.TryAgain or SocketError.NoData })
            return "DNS: не удалось определить адрес сервера. Проверьте DNS и состояние туннеля.";
        if (error?.HttpRequestError == HttpRequestError.SecureConnectionError || exception.GetBaseException() is AuthenticationException)
            return "TLS: не удалось установить защищённое соединение. Проверьте дату, сертификаты и фильтрацию трафика.";
        if (exception is OperationCanceledException or TimeoutException)
            return "Истекло время ожидания сети. Проверьте доступность сервера и текущее VPN-соединение.";
        if (error?.HttpRequestError == HttpRequestError.ConnectionError || exception.GetBaseException() is SocketException)
            return "TCP: соединение с сервером не установлено или прервано. Проверьте VPN, прокси и сетевые фильтры.";
        return exception is HttpRequestException ? "Сетевая ошибка при загрузке. Подробности доступны в журнале." : exception.Message;
    }
}
