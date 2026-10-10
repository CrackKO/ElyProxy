using System.Globalization;
using Microsoft.Data.Sqlite;
using SevenLive.Models;

namespace SevenLive.Data;

public sealed partial class SnapshotStore : IDisposable
{
    private readonly SqliteConnection _connection;

    public SnapshotStore(string databasePath)
    {
        var fullPath = Path.GetFullPath(databasePath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString();

        _connection = new SqliteConnection(connectionString);
        _connection.Open();

        Execute("PRAGMA journal_mode = WAL;");
        Execute("PRAGMA synchronous = NORMAL;");
        Execute("PRAGMA foreign_keys = ON;");
        Execute("PRAGMA busy_timeout = 5000;");

        CreateSchema();
        CreateJournalSchema();
    }

    public string DatabasePath => _connection.DataSource;

    public long UpsertWindow(WindowInfo window, DateTime utcNow)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            INSERT INTO windows (hwnd, pid, process_name, title, is_active, first_seen_utc, last_seen_utc)
            VALUES ($hwnd, $pid, $processName, $title, $isActive, $now, $now)
            ON CONFLICT(hwnd, pid) DO UPDATE SET
                process_name = excluded.process_name,
                title = excluded.title,
                is_active = excluded.is_active,
                last_seen_utc = excluded.last_seen_utc
            RETURNING id;
            """;
        command.Parameters.AddWithValue("$hwnd", window.Handle.ToInt64());
        command.Parameters.AddWithValue("$pid", window.ProcessId);
        command.Parameters.AddWithValue("$processName", window.ProcessName);
        command.Parameters.AddWithValue("$title", window.Title);
        command.Parameters.AddWithValue("$isActive", window.IsActive ? 1 : 0);
        command.Parameters.AddWithValue("$now", utcNow.ToString("o", CultureInfo.InvariantCulture));
        return Convert.ToInt64(command.ExecuteScalar() ?? 0L, CultureInfo.InvariantCulture);
    }

    public long SaveSnapshot(long windowId, WindowSnapshot snapshot)
    {
        using var transaction = _connection.BeginTransaction();

        long snapshotId;
        using (var command = _connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO snapshots (window_id, captured_utc, content_hash, element_count, is_active)
                VALUES ($windowId, $capturedUtc, $contentHash, $elementCount, $isActive)
                RETURNING id;
                """;
            command.Parameters.AddWithValue("$windowId", windowId);
            command.Parameters.AddWithValue("$capturedUtc", snapshot.CapturedUtc.ToString("o", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$contentHash", snapshot.ContentHash);
            command.Parameters.AddWithValue("$elementCount", snapshot.Elements.Count);
            command.Parameters.AddWithValue("$isActive", snapshot.IsActive ? 1 : 0);
            snapshotId = Convert.ToInt64(command.ExecuteScalar() ?? 0L, CultureInfo.InvariantCulture);
        }

        var elementRows = new Dictionary<int, long>(snapshot.Elements.Count);
        using (var command = _connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO elements (
                    snapshot_id, parent_element_id, depth, control_type, name, automation_id,
                    class_name, value, x, y, width, height, is_enabled)
                VALUES (
                    $snapshotId, $parentId, $depth, $controlType, $name, $automationId,
                    $className, $value, $x, $y, $width, $height, $isEnabled)
                RETURNING id;
                """;

            var snapshotIdParameter = command.Parameters.Add("$snapshotId", SqliteType.Integer);
            var parentIdParameter = command.Parameters.Add("$parentId", SqliteType.Integer);
            var depthParameter = command.Parameters.Add("$depth", SqliteType.Integer);
            var controlTypeParameter = command.Parameters.Add("$controlType", SqliteType.Text);
            var nameParameter = command.Parameters.Add("$name", SqliteType.Text);
            var automationIdParameter = command.Parameters.Add("$automationId", SqliteType.Text);
            var classNameParameter = command.Parameters.Add("$className", SqliteType.Text);
            var valueParameter = command.Parameters.Add("$value", SqliteType.Text);
            var xParameter = command.Parameters.Add("$x", SqliteType.Integer);
            var yParameter = command.Parameters.Add("$y", SqliteType.Integer);
            var widthParameter = command.Parameters.Add("$width", SqliteType.Integer);
            var heightParameter = command.Parameters.Add("$height", SqliteType.Integer);
            var isEnabledParameter = command.Parameters.Add("$isEnabled", SqliteType.Integer);

            snapshotIdParameter.Value = snapshotId;

            foreach (var element in snapshot.Elements)
            {
                parentIdParameter.Value = element.ParentElementId is int parentId && elementRows.TryGetValue(parentId, out var parentRowId)
                    ? parentRowId
                    : DBNull.Value;
                depthParameter.Value = element.Depth;
                controlTypeParameter.Value = element.ControlType;
                nameParameter.Value = ToDatabaseValue(element.Name);
                automationIdParameter.Value = ToDatabaseValue(element.AutomationId);
                classNameParameter.Value = ToDatabaseValue(element.ClassName);
                valueParameter.Value = ToDatabaseValue(element.Value);
                xParameter.Value = element.Bounds.X;
                yParameter.Value = element.Bounds.Y;
                widthParameter.Value = element.Bounds.Width;
                heightParameter.Value = element.Bounds.Height;
                isEnabledParameter.Value = element.IsEnabled ? 1 : 0;

                elementRows[element.ElementId] = Convert.ToInt64(command.ExecuteScalar() ?? 0L, CultureInfo.InvariantCulture);
            }
        }

        transaction.Commit();
        return snapshotId;
    }

    public (long Windows, long Snapshots, long Elements) ReadCounts()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT
                (SELECT COUNT(*) FROM windows),
                (SELECT COUNT(*) FROM snapshots),
                (SELECT COUNT(*) FROM elements);
            """;
        using var reader = command.ExecuteReader();
        return reader.Read()
            ? (reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2))
            : (0L, 0L, 0L);
    }

    public IReadOnlyList<SnapshotSummary> ReadRecentSnapshots(int limit)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT s.id, s.captured_utc, w.process_name, w.title, s.content_hash, s.element_count, s.is_active
            FROM snapshots s
            JOIN windows w ON w.id = s.window_id
            ORDER BY s.id DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", limit);

        var result = new List<SnapshotSummary>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(ReadSummary(reader));
        }

        return result;
    }

    public SnapshotSummary? ReadSnapshot(long snapshotId)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT s.id, s.captured_utc, w.process_name, w.title, s.content_hash, s.element_count, s.is_active
            FROM snapshots s
            JOIN windows w ON w.id = s.window_id
            WHERE s.id = $id;
            """;
        command.Parameters.AddWithValue("$id", snapshotId);

        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadSummary(reader) : null;
    }

    public IReadOnlyList<StoredElement> ReadSnapshotElements(long snapshotId, int limit)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT id, snapshot_id, parent_element_id, depth, control_type, name, automation_id,
                   class_name, value, x, y, width, height, is_enabled
            FROM elements
            WHERE snapshot_id = $id
            ORDER BY id
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$id", snapshotId);
        command.Parameters.AddWithValue("$limit", limit);

        var result = new List<StoredElement>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new StoredElement(
                reader.GetInt64(0),
                reader.GetInt64(1),
                reader.IsDBNull(2) ? null : reader.GetInt64(2),
                reader.GetInt32(3),
                reader.GetString(4),
                ReadNullableString(reader, 5),
                ReadNullableString(reader, 6),
                ReadNullableString(reader, 7),
                ReadNullableString(reader, 8),
                reader.GetInt32(9),
                reader.GetInt32(10),
                reader.GetInt32(11),
                reader.GetInt32(12),
                reader.GetInt32(13) != 0));
        }

        return result;
    }

    public IReadOnlyList<ElementSearchHit> SearchElements(string query, int limit)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT s.id, s.captured_utc, w.process_name, w.title, e.control_type, e.name, e.value
            FROM elements e
            JOIN snapshots s ON s.id = e.snapshot_id
            JOIN windows w ON w.id = s.window_id
            WHERE e.name LIKE $query OR e.value LIKE $query OR e.automation_id LIKE $query
            ORDER BY s.id DESC, e.id
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$query", "%" + query + "%");
        command.Parameters.AddWithValue("$limit", limit);

        var result = new List<ElementSearchHit>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new ElementSearchHit(
                reader.GetInt64(0),
                ParseUtc(reader.GetString(1)),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                ReadNullableString(reader, 5),
                ReadNullableString(reader, 6)));
        }

        return result;
    }

    public StoreStatistics ReadStatistics()
    {
        var counts = ReadCounts();

        DateTime? first = null;
        DateTime? last = null;
        using (var command = _connection.CreateCommand())
        {
            command.CommandText = "SELECT MIN(captured_utc), MAX(captured_utc) FROM snapshots;";
            using var reader = command.ExecuteReader();
            if (reader.Read() && !reader.IsDBNull(0))
            {
                first = ParseUtc(reader.GetString(0));
                last = ParseUtc(reader.GetString(1));
            }
        }

        var topProcesses = new List<ProcessSnapshotCount>();
        using (var command = _connection.CreateCommand())
        {
            command.CommandText = """
                SELECT w.process_name, COUNT(*) AS snapshot_count
                FROM snapshots s
                JOIN windows w ON w.id = s.window_id
                GROUP BY w.process_name
                ORDER BY snapshot_count DESC
                LIMIT 10;
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                topProcesses.Add(new ProcessSnapshotCount(reader.GetString(0), reader.GetInt64(1)));
            }
        }

        var size = File.Exists(DatabasePath) ? new FileInfo(DatabasePath).Length : 0;
        return new StoreStatistics(counts.Windows, counts.Snapshots, counts.Elements, first, last, size, topProcesses);
    }

    public long DeleteSnapshotsOlderThan(DateTime cutoffUtc)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "DELETE FROM snapshots WHERE captured_utc < $cutoff;";
        command.Parameters.AddWithValue("$cutoff", cutoffUtc.ToString("o", CultureInfo.InvariantCulture));
        return command.ExecuteNonQuery();
    }

    public long DeleteOrphanWindows()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "DELETE FROM windows WHERE id NOT IN (SELECT DISTINCT window_id FROM snapshots);";
        return command.ExecuteNonQuery();
    }

    public void Vacuum()
    {
        Execute("VACUUM;");
    }

    public void Dispose()
    {
        _connection.Dispose();
    }

    private void CreateSchema()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS windows (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                hwnd INTEGER NOT NULL,
                pid INTEGER NOT NULL,
                process_name TEXT NOT NULL,
                title TEXT NOT NULL,
                is_active INTEGER NOT NULL DEFAULT 0,
                first_seen_utc TEXT NOT NULL,
                last_seen_utc TEXT NOT NULL
            );

            CREATE UNIQUE INDEX IF NOT EXISTS ux_windows_hwnd_pid ON windows(hwnd, pid);

            CREATE TABLE IF NOT EXISTS snapshots (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                window_id INTEGER NOT NULL REFERENCES windows(id) ON DELETE CASCADE,
                captured_utc TEXT NOT NULL,
                content_hash TEXT NOT NULL,
                element_count INTEGER NOT NULL,
                is_active INTEGER NOT NULL
            );

            CREATE INDEX IF NOT EXISTS ix_snapshots_window ON snapshots(window_id, id);

            CREATE TABLE IF NOT EXISTS elements (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                snapshot_id INTEGER NOT NULL REFERENCES snapshots(id) ON DELETE CASCADE,
                parent_element_id INTEGER NULL REFERENCES elements(id),
                depth INTEGER NOT NULL,
                control_type TEXT NOT NULL,
                name TEXT NULL,
                automation_id TEXT NULL,
                class_name TEXT NULL,
                value TEXT NULL,
                x INTEGER NOT NULL,
                y INTEGER NOT NULL,
                width INTEGER NOT NULL,
                height INTEGER NOT NULL,
                is_enabled INTEGER NOT NULL
            );

            CREATE INDEX IF NOT EXISTS ix_elements_snapshot ON elements(snapshot_id);
            """;
        command.ExecuteNonQuery();
    }

    private void Execute(string sql)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static SnapshotSummary ReadSummary(SqliteDataReader reader)
    {
        return new SnapshotSummary(
            reader.GetInt64(0),
            ParseUtc(reader.GetString(1)),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetInt32(5),
            reader.GetInt32(6) != 0);
    }

    private static DateTime ParseUtc(string value)
    {
        return DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }

    private static string? ReadNullableString(SqliteDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static object ToDatabaseValue(string? value)
    {
        return string.IsNullOrEmpty(value) ? DBNull.Value : value;
    }
}
