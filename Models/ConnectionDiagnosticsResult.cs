namespace ElyProxy.Models;

public sealed record ConnectionDiagnosticCheck(
    string Name,
    bool Passed,
    string Details,
    string? SuggestedAction = null);

public sealed record ConnectionDiagnosticsResult(
    string Mode,
    IReadOnlyList<ConnectionDiagnosticCheck> Checks)
{
    public bool RoutePassed => Checks.LastOrDefault(check => check.Name == "Внешний маршрут")?.Passed == true;

    public string ToSafeReport()
    {
        string lines = string.Join(
            Environment.NewLine,
            Checks.Select(check =>
                $"{(check.Passed ? "✓" : "✗")} {check.Name}: {check.Details}"
                + (string.IsNullOrWhiteSpace(check.SuggestedAction)
                    ? string.Empty
                    : $"{Environment.NewLine}  Следующий шаг: {check.SuggestedAction}")));
        return $"Режим: {Mode}{Environment.NewLine}{lines}{Environment.NewLine}{Environment.NewLine}В отчёт не включены ссылки подписок, UUID и адреса серверов.";
    }
}
