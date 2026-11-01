using ElyProxy.Models;

namespace ElyProxy.Core;

public static class ProxyChainPolicy
{
    public static void Validate(VlessNode primary, VlessNode exit)
    {
        if (exit.Protocol is not ("socks" or "http") || string.IsNullOrWhiteSpace(exit.Address) || exit.Port is < 1 or > 65535)
            throw new NotSupportedException("Выходной прокси должен быть SOCKS5, HTTP или HTTPS с корректным адресом и портом.");
        if (primary.Protocol is not ("vless" or "vmess" or "trojan" or "shadowsocks" or "ss" or "socks" or "http" or "hysteria2" or "hy2" or "tuic" or "anytls" or "ssh"))
            throw new NotSupportedException("Цепочка пока не поддерживает этот основной протокол. Используйте VLESS, VMess, Trojan, Shadowsocks, Hysteria2, TUIC, AnyTLS, SSH или обычный прокси.");
        if (string.Equals(primary.Address, exit.Address, StringComparison.OrdinalIgnoreCase) && primary.Port == exit.Port)
            throw new NotSupportedException("Основной сервер и выходной прокси должны иметь разные адреса или порты.");
    }
}
