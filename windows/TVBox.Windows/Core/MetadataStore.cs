using Microsoft.Data.Sqlite;

namespace TVBoxForWindows.Core;

public record MetadataCacheEntry<T>(T Value, DateTimeOffset WrittenAt, DateTimeOffset ExpiresAt)
{
    public bool IsFresh => DateTimeOffset.UtcNow < ExpiresAt;
}
/// <summary>Additive metadata database. Existing history/keep/config files remain authoritative.</summary>
public sealed class MetadataStore
{
    static MetadataStore _instance;
    public static MetadataStore Instance => _instance ??= CreateDefault();
    static MetadataStore CreateDefault()
    {
        var path = Path.Combine(AppPaths.Root, "metadata-v4.sqlite");
        try { return new(path); }
        catch (SqliteException)
        {
            // Preserve the unreadable database, including mappings/presets, for recovery.
            return new(Path.Combine(AppPaths.Root, "metadata-v4-recovery.sqlite")) { RecoveryNotice = "原资料缓存暂不可读，使用独立恢复缓存；原文件、收藏和历史保留" };
        }
    }
    public string RecoveryNotice { get; private set; } = "";
    readonly object _sync = new();
    readonly string _path;
    public MetadataStore(string path)
    {
        _path = path; Directory.CreateDirectory(Path.GetDirectoryName(path));
        using var db = Open();
        using var command = db.CreateCommand();
        command.CommandText = "PRAGMA auto_vacuum=INCREMENTAL; PRAGMA journal_mode=WAL; CREATE TABLE IF NOT EXISTS objects(kind TEXT NOT NULL,id TEXT NOT NULL,json TEXT NOT NULL,written INTEGER NOT NULL,expires INTEGER NOT NULL,PRIMARY KEY(kind,id)); CREATE INDEX IF NOT EXISTS objects_age ON objects(kind,written);";
        command.ExecuteNonQuery();
    }
    SqliteConnection Open()
    {
        var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _path, Pooling = true }.ToString());
        db.Open(); using var cmd = db.CreateCommand(); cmd.CommandText = "PRAGMA busy_timeout=3000"; cmd.ExecuteNonQuery(); return db;
    }
    public MetadataCacheEntry<T> Read<T>(string kind, string id)
    {
        lock (_sync)
        {
            using var db = Open(); using var cmd = db.CreateCommand();
            cmd.CommandText = "SELECT json,written,expires FROM objects WHERE kind=$kind AND id=$id";
            cmd.Parameters.AddWithValue("$kind", kind); cmd.Parameters.AddWithValue("$id", id);
            using var reader = cmd.ExecuteReader(); if (!reader.Read()) return null;
            try { var value = JsonUtil.Deserialize<T>(reader.GetString(0)); return value is null ? null : new(value, DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(1)), DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(2))); }
            catch (System.Text.Json.JsonException) { return null; }
        }
    }
    int _trimPending;
    public void Write<T>(string kind, string id, T value, TimeSpan? ttl = null) => WriteBatch((kind, id, value, ttl));
    public void WriteBatch(params (string Kind, string Id, object Value, TimeSpan? Ttl)[] values)
    {
        // Serialize before opening the transaction; related cache updates commit once.
        var now = DateTimeOffset.UtcNow;
        var rows = values.Select(v => (v.Kind, v.Id, Json: JsonUtil.Serialize(v.Value), Expires: v.Ttl == null ? DateTimeOffset.MaxValue.ToUnixTimeSeconds() : (now + v.Ttl.Value).ToUnixTimeSeconds())).ToList();
        lock (_sync)
        {
            using var db = Open(); using var tx = db.BeginTransaction();
            foreach (var row in rows)
            {
                using var cmd = db.CreateCommand(); cmd.Transaction = tx;
                cmd.CommandText = "INSERT INTO objects(kind,id,json,written,expires) VALUES($kind,$id,$json,$now,$expires) ON CONFLICT(kind,id) DO UPDATE SET json=excluded.json,written=excluded.written,expires=excluded.expires";
                cmd.Parameters.AddWithValue("$kind", row.Kind); cmd.Parameters.AddWithValue("$id", row.Id); cmd.Parameters.AddWithValue("$json", row.Json);
                cmd.Parameters.AddWithValue("$now", now.ToUnixTimeSeconds()); cmd.Parameters.AddWithValue("$expires", row.Expires); cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }
        if (CacheBytes > 128L * 1024 * 1024 && Interlocked.CompareExchange(ref _trimPending, 1, 0) == 0)
            _ = Task.Run(() => { try { Trim(); } catch (SqliteException) { } finally { Volatile.Write(ref _trimPending, 0); } });
    }
    public void Delete(string kind, string id)
    {
        lock (_sync)
        {
            using var db = Open(); using var cmd = db.CreateCommand(); cmd.CommandText = "DELETE FROM objects WHERE kind=$kind AND id=$id";
            cmd.Parameters.AddWithValue("$kind", kind); cmd.Parameters.AddWithValue("$id", id); cmd.ExecuteNonQuery();
        }
    }
    public List<(string Id, T Value)> List<T>(string kind)
    {
        lock (_sync)
        {
            using var db = Open(); using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT id,json FROM objects WHERE kind=$kind ORDER BY written DESC"; cmd.Parameters.AddWithValue("$kind", kind);
            using var reader = cmd.ExecuteReader(); var result = new List<(string, T)>();
            while (reader.Read()) { try { var value = JsonUtil.Deserialize<T>(reader.GetString(1)); if (value is not null) result.Add((reader.GetString(0), value)); } catch (System.Text.Json.JsonException) { } }
            return result;
        }
    }
    public long CacheBytes => new[] { _path, _path + "-wal", _path + "-shm" }.Sum(p => { try { return new FileInfo(p).Length; } catch { return 0; } });
    public void ClearCache()
    {
        lock (_sync)
        {
            using var db = Open(); using var cmd = db.CreateCommand();
            cmd.CommandText = "DELETE FROM objects WHERE kind IN ('work','work-detail','query','response'); PRAGMA wal_checkpoint(TRUNCATE); PRAGMA incremental_vacuum(8192)"; cmd.ExecuteNonQuery();
        }
    }
    void Trim()
    {
        lock (_sync)
        {
            using var db = Open(); using var cmd = db.CreateCommand();
            cmd.CommandText = "DELETE FROM objects WHERE kind IN ('work','work-detail','response') AND written < strftime('%s','now','-30 days'); DELETE FROM objects WHERE kind='query' AND id NOT IN (SELECT id FROM objects WHERE kind='query' ORDER BY written DESC LIMIT 40); DELETE FROM objects WHERE kind IN ('work','work-detail') AND id NOT IN (SELECT id FROM objects WHERE kind IN ('work','work-detail') ORDER BY written DESC LIMIT 2000); PRAGMA wal_checkpoint(TRUNCATE); PRAGMA incremental_vacuum(8192)";
            cmd.ExecuteNonQuery();
        }
    }
}
