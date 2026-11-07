namespace TVBoxForWindows.Core;

public record CacheCleanupResult(long Before, long After, int Deleted);
public static class CacheMaintenance
{
    static Timer _timer;
    static int _running;
    public static long LimitBytes => Math.Clamp(Setting.GetInt("cache_limit_mb", 1024), 128, 8192) * 1024L * 1024;
    public static void Start()
    {
        _timer ??= new Timer(_ => Schedule(), null, TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(5));
    }
    public static void Schedule()
    {
        if (!Setting.GetBool("cache_auto_clean", true) || Interlocked.CompareExchange(ref _running, 1, 0) != 0) return;
        _ = Task.Run(() => { try { Clean(AppPaths.Cache, LimitBytes, DateTime.UtcNow); } catch (IOException) { } catch (UnauthorizedAccessException) { } finally { Volatile.Write(ref _running, 0); } });
    }
    public static CacheCleanupResult Clean(string directory, long limit, DateTime now)
    {
        var root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!Directory.Exists(root)) return new(0, 0, 0);
        if ((new DirectoryInfo(root).Attributes & FileAttributes.ReparsePoint) != 0) return new(0, 0, 0);
        var files = new DirectoryInfo(root).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = true })
            .Where(f => f.FullName.StartsWith(root, StringComparison.OrdinalIgnoreCase)).OrderBy(f => f.LastWriteTimeUtc).ToList();
        long before = files.Sum(f => f.Length), left = before; var count = 0;
        if (left <= limit) return new(before, left, 0);
        foreach (var file in files.Where(f => f.LastWriteTimeUtc < now.AddMinutes(-2)))
        {
            if (left <= limit * 0.8) break;
            try { var size = file.Length; file.Delete(); left -= size; count++; } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        return new(before, left, count);
    }
}
