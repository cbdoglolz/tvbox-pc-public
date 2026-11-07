namespace TVBoxForWindows.Core;

/// <summary>Local session attribution, without film names, addresses or credentials.</summary>
public static class AppSessionDiagnostics
{
    static string _path;
    static readonly object Sync = new();
    static Session _session;
    public sealed record Session(int Pid, string Version, DateTimeOffset StartedAt, string State, DateTimeOffset? EndedAt = null, string ExceptionType = "", int? HResult = null);
    public static void Start()
    {
        var now = DateTimeOffset.Now;
        _path = Path.Combine(AppPaths.Root, "diagnostics", $"session-{now:yyyyMMdd-HHmmss}-{Environment.ProcessId}.json");
        _session = new(Environment.ProcessId, typeof(AppSessionDiagnostics).Assembly.GetName().Version?.ToString() ?? "", now, "running");
        Save(); Logger.D("Lifecycle", "Session started; local diagnostics enabled");
        AppDomain.CurrentDomain.UnhandledException += (_, e) => {
            if (e.ExceptionObject is Exception exception) Record(exception, "fatal-managed");
        };
        TaskScheduler.UnobservedTaskException += (_, e) => Record(e.Exception, "unobserved-task");
    }
    public static void Record(Exception error, string state = "handled-ui-error")
    {
        if (error == null) return;
        lock (Sync)
        {
            if (_session == null) return;
            _session = _session with { State = state, ExceptionType = error.GetType().FullName, HResult = error.HResult };
            Save();
        }
    }
    public static void Close()
    {
        lock (Sync)
        {
            if (_session == null) return;
            _session = _session with { State = "closed", EndedAt = DateTimeOffset.Now }; Save();
        }
    }
    static void Save()
    {
        try { DurableJsonFile.Write(_path, JsonUtil.Serialize(_session)); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
