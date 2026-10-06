namespace ElyProxy.Models;

public sealed record RuntimePreflightCheck(string Name, bool Ready, string Details);

public sealed record RuntimePreflightResult(IReadOnlyList<RuntimePreflightCheck> Checks)
{
    public bool HasBlockingIssue => Checks.Any(check => !check.Ready);

    public string ToReport() => string.Join(
        Environment.NewLine,
        Checks.Select(check => $"{(check.Ready ? "✓" : "✗")} {check.Name}: {check.Details}"));
}
