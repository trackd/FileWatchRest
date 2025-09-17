using System.Collections.Concurrent;

namespace FileWatchRest.Services;

public record FileEventRecord(string Path, DateTimeOffset Timestamp, bool PostedSuccess, int? StatusCode);

public class DiagnosticsService
{
    private readonly ConcurrentDictionary<string, int> _restartAttempts = new();
    private readonly ConcurrentDictionary<string, byte> _activeWatchers = new();
    private readonly ConcurrentQueue<FileEventRecord> _events = new();

    public void RegisterWatcher(string folder)
    {
        _activeWatchers[folder] = 1;
    }

    public void UnregisterWatcher(string folder)
    {
        _activeWatchers.TryRemove(folder, out _);
    }

    public int IncrementRestart(string folder)
    {
        return _restartAttempts.AddOrUpdate(folder, 1, (_, cur) => cur + 1);
    }

    public void ResetRestart(string folder)
    {
        _restartAttempts.TryRemove(folder, out _);
    }

    public IReadOnlyDictionary<string, int> GetRestartAttemptsSnapshot() => new Dictionary<string, int>(_restartAttempts);

    public IReadOnlyCollection<string> GetActiveWatchers() => _activeWatchers.Keys.ToList().AsReadOnly();

    public void RecordFileEvent(string path, bool postedSuccess, int? statusCode)
    {
        _events.Enqueue(new FileEventRecord(path, DateTimeOffset.UtcNow, postedSuccess, statusCode));
        // keep the queue bounded to e.g. 1000 entries
        while (_events.Count > 1000 && _events.TryDequeue(out _)) { }
    }

    public IReadOnlyCollection<FileEventRecord> GetRecentEvents(int limit = 100)
    {
        return _events.Reverse().Take(limit).ToArray();
    }

    public object GetStatus()
    {
        return new {
            ActiveWatchers = GetActiveWatchers(),
            RestartAttempts = GetRestartAttemptsSnapshot(),
            RecentEvents = GetRecentEvents(200)
        };
    }
}
