using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ElyProxy.Models;

namespace ElyProxy.Services;

public sealed record CorePerformanceRecord
{
    public int StartedConnections { get; init; }
    public int ValidatedConnections { get; init; }
    public int InconclusiveValidations { get; init; }
    public double LastValidationMs { get; init; }
    public int Successes { get; init; }
    public int Failures { get; init; }
    public int ConsecutiveFailures { get; init; }
    public DateTimeOffset? LastSuccess { get; init; }
    public DateTimeOffset? LastFailure { get; init; }
    public string? LastFailureCategory { get; init; }
    public DateTimeOffset LastUsed { get; init; }
    public double AverageStartupMs { get; init; }
    public double LastStartupMs { get; init; }
    public double? AverageProbeLatencyMs { get; init; }
    public double Reliability => (ValidatedConnections + 2.0) / (StartedConnections + Failures + 4.0);
}

/// <summary>Best-effort, bounded local telemetry. Persistence failure never blocks a connection.</summary>
public sealed class CorePerformanceHistoryService
{
    private readonly object _gate = new();
    private readonly string _path;
    private Dictionary<string, CorePerformanceRecord> _records = new();
    public CorePerformanceHistoryService(string? path = null)
    {
        _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ElyProxy", "core-history.json");
        try
        {
            if (File.Exists(_path) && new FileInfo(_path).Length <= 2_000_000)
                _records = JsonSerializer.Deserialize<Dictionary<string, CorePerformanceRecord>>(File.ReadAllText(_path)) ?? new();
            Cleanup();
        }
        catch { _records = new(); }
    }
    public static string Fingerprint(VlessNode node, string mode, string core)
    {
        // Canonical property ordering includes credentials only inside the one-way digest.
        var fields = typeof(VlessNode).GetProperties().Where(p => p.CanRead && p.GetIndexParameters().Length == 0
            && p.Name is not ("Name" or "LibraryId") && p.CanWrite)
            .OrderBy(p => p.Name).ToDictionary(p => p.Name, p => p.Name == "Extra"
                ? (object)node.Extra.OrderBy(e => e.Key).ToArray() : p.GetValue(node));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { fields, mode, core }))));
    }
    public CorePerformanceRecord? Get(string key) { lock (_gate) return _records.GetValueOrDefault(key); }
    public void Record(string key, bool success, double startupMs, string? category = null, DateTimeOffset? now = null, bool? validated = null, double validationMs = 0)
    {
        lock (_gate)
        {
            var time = now ?? DateTimeOffset.UtcNow;
            var old = _records.GetValueOrDefault(key) ?? new();
            startupMs = double.IsFinite(startupMs) ? Math.Clamp(startupMs, 0, 300_000) : 0;
            _records[key] = old with {
                StartedConnections = old.StartedConnections + (success ? 1 : 0),
                ValidatedConnections = old.ValidatedConnections + (success && validated == true ? 1 : 0),
                InconclusiveValidations = old.InconclusiveValidations + (success && validated != true ? 1 : 0),
                LastValidationMs = double.IsFinite(validationMs) ? Math.Max(0, validationMs) : 0,
                Successes = Math.Min(1000000, old.Successes + (success ? 1 : 0)),
                Failures = Math.Min(1000000, old.Failures + (success ? 0 : 1)),
                ConsecutiveFailures = success ? 0 : Math.Min(100, old.ConsecutiveFailures + 1),
                LastSuccess = success ? time : old.LastSuccess, LastFailure = success ? old.LastFailure : time,
                LastFailureCategory = success ? old.LastFailureCategory : category, LastUsed = time,
                LastStartupMs = startupMs, AverageStartupMs = success ? old.Successes == 0 ? startupMs : old.AverageStartupMs * .8 + startupMs * .2 : old.AverageStartupMs };
            Cleanup();
            string temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!); File.WriteAllText(temporary, JsonSerializer.Serialize(_records)); File.Move(temporary, _path, true); }
            catch { }
            finally { try { File.Delete(temporary); } catch { } }
        }
    }
    private void Cleanup() => _records = _records.Where(e => e.Value != null && e.Value.LastUsed > DateTimeOffset.UtcNow.AddDays(-90))
        .OrderByDescending(e => e.Value.LastUsed).Take(512).ToDictionary(e => e.Key, e => e.Value);
}

