using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ElyProxy.Helper.Runtime;

namespace ElyProxy.Helper;

internal sealed class OwnedTunSession
{
    internal sealed record State(string Name, string? Guid, int ProcessId, long StartedUtcTicks);
    private readonly string? _name;
    private readonly string _journal;
    private State? _state;
    private bool _cleaned;
    private OwnedTunSession(string? name)
    {
        _name = name;
        _journal = Path.Combine(JournalDirectory, System.Guid.NewGuid().ToString("N") + ".json");
    }
    private static string JournalDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ElyProxy", "tun-sessions");
    internal static bool IsOwnedName(string? name) => name != null && Regex.IsMatch(name, "^ElyTun-[0-9a-fA-F]{8}$");
    internal static string? FindName(CoreLaunch launch)
    {
        var argument = Regex.Match(launch.Arguments, "--tun\\s+\"(ElyTun-[0-9a-fA-F]{8})\"");
        if (argument.Success) return argument.Groups[1].Value;
        var path = Regex.Match(launch.Arguments, "(?:-c|-config|--config)\\s+\"([^\"]+)\"");
        if (!path.Success || !File.Exists(path.Groups[1].Value)) return null;
        var text = File.ReadAllText(path.Groups[1].Value);
        if (text.TrimStart().StartsWith('{'))
        {
            using var json = JsonDocument.Parse(text);
            if (!json.RootElement.TryGetProperty("inbounds", out var inbounds)) return null;
            foreach (var inbound in inbounds.EnumerateArray())
            {
                string? kind = GetString(inbound, "type") ?? GetString(inbound, "protocol");
                if (kind != "tun") continue;
                var name = GetString(inbound, "interface_name");
                if (name == null && inbound.TryGetProperty("settings", out var settings)) name = GetString(settings, "name");
                if (IsOwnedName(name)) return name;
            }
            return null;
        }
        var yaml = Regex.Match(text, "(?m)^tun:\\s*\\r?\\n\\s+name:\\s*\"(ElyTun-[0-9a-fA-F]{8})\"");
        return yaml.Success ? yaml.Groups[1].Value : null;
    }
    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    public static async Task<OwnedTunSession> PrepareAsync(CoreLaunch launch)
    {
        var session = new OwnedTunSession(FindName(launch));
        if (session._name != null)
        {
            await RecoverAsync().ConfigureAwait(false);
            if (await AdapterGuidAsync(session._name).ConfigureAwait(false) is not null)
                throw new IOException("Имя TUN уже занято. Запуск поверх существующего адаптера запрещён.");
        }
        return session;
    }
    public async Task TrackAsync(Process process)
    {
        if (_name == null) return;
        _state = new State(_name, null, process.Id, process.StartTime.ToUniversalTime().Ticks);
        Save();
        // Capture the adapter identity while the owning process is alive.
        for (int i = 0; i < 4 && !process.HasExited; i++)
        {
            if (await AdapterGuidAsync(_name).ConfigureAwait(false) is { } guid)
            {
                _state = _state with { Guid = guid }; Save(); return;
            }
            await Task.Delay(150).ConfigureAwait(false);
        }
    }
    private void Save()
    {
        Directory.CreateDirectory(JournalDirectory);
        var temporary = _journal + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(_state));
        File.Move(temporary, _journal, true);
    }
    public async Task CleanupAsync()
    {
        if (_cleaned || _state == null) return;
        if (IsAlive(_state)) throw new IOException("Восстановление TUN запрещено: владелец ещё работает.");
        if (_state.Guid != null) await CleanAdapterAsync(_state).ConfigureAwait(false);
        else if (await AdapterGuidAsync(_state.Name).ConfigureAwait(false) != null)
            throw new IOException("Адаптер TUN остался, но его GUID не был подтверждён. Автоматическое удаление запрещено.");
        File.Delete(_journal); _cleaned = true;
    }
    private static bool IsAlive(State state)
    {
        try { using var p = Process.GetProcessById(state.ProcessId); return !p.HasExited && p.StartTime.ToUniversalTime().Ticks == state.StartedUtcTicks; }
        catch (ArgumentException) { return false; }
    }
    internal static bool CanClean(State state, string guid, string name, bool hardware, bool ownerAlive) =>
        IsOwnedName(state.Name) && !ownerAlive && !hardware && state.Name == name && string.Equals(state.Guid, guid, StringComparison.OrdinalIgnoreCase);
    private static async Task CleanAdapterAsync(State state)
    {
        if (!IsOwnedName(state.Name) || !System.Guid.TryParse(state.Guid, out _)) throw new InvalidDataException("Invalid TUN identity.");
        // No wildcard deletes, no TCP/IP reset, and no physical/foreign adapters.
        string script = "$a=Get-NetAdapter -IncludeHidden -ErrorAction SilentlyContinue | Where-Object { $_.InterfaceGuid -eq '" + state.Guid + "' -and $_.Name -eq '" + state.Name + "' -and -not $_.HardwareInterface }; " +
            "if($a){ $a | ForEach-Object { Get-NetRoute -InterfaceIndex $_.ifIndex -ErrorAction SilentlyContinue | Remove-NetRoute -Confirm:$false -ErrorAction Stop; Set-DnsClientServerAddress -InterfaceIndex $_.ifIndex -ResetServerAddresses -ErrorAction Stop; Disable-NetAdapter -InputObject $_ -Confirm:$false -ErrorAction Stop } }";
        try { await PowerShellAsync(script).ConfigureAwait(false); }
        catch (IOException)
        {
            // The driver may remove the adapter between enumeration and DNS/route cleanup.
            // A different/new GUID must also never be treated as our old session.
            var remaining = await AdapterGuidAsync(state.Name).ConfigureAwait(false);
            if (!string.Equals(remaining, state.Guid, StringComparison.OrdinalIgnoreCase)) return;
            throw;
        }
    }
    private static async Task<string?> AdapterGuidAsync(string name)
    {
        if (!IsOwnedName(name)) return null;
        var value = (await PowerShellAsync("Get-NetAdapter -IncludeHidden -ErrorAction SilentlyContinue | Where-Object { $_.Name -eq '" + name + "' } | Select-Object -ExpandProperty InterfaceGuid").ConfigureAwait(false)).Trim();
        return System.Guid.TryParse(value, out var guid) ? guid.ToString() : null;
    }
    private static async Task<string> PowerShellAsync(string script)
    {
        var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-NonInteractive"); start.ArgumentList.Add("-EncodedCommand");
        start.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes("$ErrorActionPreference='Stop'; " + script)));
        using var p = Process.Start(start) ?? throw new IOException("Не удалось проверить TUN.");
        var output = p.StandardOutput.ReadToEndAsync(); var error = p.StandardError.ReadToEndAsync();
        try { await p.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(4)).ConfigureAwait(false); }
        catch { if (!p.HasExited) p.Kill(true); throw; }
        if (p.ExitCode != 0) throw new IOException("Проверка/очистка TUN не выполнена: " + await error.ConfigureAwait(false));
        return await output.ConfigureAwait(false);
    }
    internal static async Task RecoverAsync()
    {
        if (!Directory.Exists(JournalDirectory)) return;
        foreach (var path in Directory.EnumerateFiles(JournalDirectory, "*.json"))
        {
            try
            {
                var state = JsonSerializer.Deserialize<State>(File.ReadAllText(path));
                if (state == null) throw new InvalidDataException("Повреждён журнал TUN.");
                if (IsAlive(state)) continue;
                if (state.Guid == null)
                {
                    if (await AdapterGuidAsync(state.Name).ConfigureAwait(false) != null)
                        throw new IOException("Остался адаптер с неподтверждённым GUID. Новый TUN заблокирован.");
                    File.Delete(path); continue;
                }
                await CleanAdapterAsync(state).ConfigureAwait(false); File.Delete(path);
            }
            catch (FileNotFoundException) { /* Another completed session already removed its journal. */ }
            catch (Exception ex) { HelperLifecycleLog.Write("tun-recovery", ex); throw new IOException("Не удалось восстановить прежнюю TUN-сессию. Новый туннель не запущен.", ex); }
        }
    }
}
