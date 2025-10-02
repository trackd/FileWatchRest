namespace FileWatchRest.Services;

/// <summary>
/// Manages a set of FileSystemWatcher instances for one or more folders, including restart attempts on error.
/// The manager wraps the OS watchers and provides simple restart logic and callback hooks.
/// </summary>
public class FileWatcherManager : IDisposable
{
    private readonly ILogger<FileWatcherManager> _logger;
    private readonly DiagnosticsService _diagnostics;
    private static readonly Action<ILogger<FileWatcherManager>, string, Exception?> _watchingFolder =
        LoggerMessage.Define<string>(LogLevel.Information, new EventId(1, "WatchingFolder"), "Watching folder: {Folder}");
    private static readonly Action<ILogger<FileWatcherManager>, string, Exception?> _failedToWatchFolder =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(2, "FailedToWatchFolder"), "Failed to watch folder {Folder}");
    private static readonly Action<ILogger<FileWatcherManager>, string, Exception?> _watcherError =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(3, "WatcherError"), "FileSystemWatcher error for folder {Folder}");
    private static readonly Action<ILogger<FileWatcherManager>, string, int, int, Exception?> _attemptingRestart =
        LoggerMessage.Define<string, int, int>(LogLevel.Information, new EventId(4, "AttemptingRestart"), "Attempting to restart watcher for {Folder} (attempt {Attempt}/{Max})");
    private static readonly Action<ILogger<FileWatcherManager>, string, int, Exception?> _watcherFailedAfterMax =
        LoggerMessage.Define<string, int>(LogLevel.Error, new EventId(5, "WatcherFailedAfterMax"), "Watcher for {Folder} failed after {Max} attempts");
    private static readonly Action<ILogger<FileWatcherManager>, string, Exception?> _watcherRestarted =
        LoggerMessage.Define<string>(LogLevel.Information, new EventId(6, "WatcherRestarted"), "Successfully restarted watcher for {Folder}");
    private static readonly Action<ILogger<FileWatcherManager>, string, Exception?> _failedHandlingWatcherError =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(7, "FailedHandlingWatcherError"), "Failed handling watcher error for {Folder}");
    private static readonly Action<ILogger<FileWatcherManager>, string, Exception?> _failedRestartWatcher =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(8, "FailedRestartWatcher"), "Failed to restart watcher for {Folder}");
    private static readonly Action<ILogger<FileWatcherManager>, string, Exception?> _errorStoppingWatcher =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(9, "ErrorStoppingWatcher"), "Error stopping watcher for {Path}");
    private readonly ConcurrentDictionary<string, FileSystemWatcher> _watchers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, int> _restartAttempts = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();
    // For testing: allow registering folder metadata and simulate watcher errors without OS watchers
    private readonly ConcurrentDictionary<string, FolderInfo> _folderInfos = new(StringComparer.OrdinalIgnoreCase);

    private sealed class FolderInfo
    {
        public ExternalConfiguration? Config;
        public FileSystemEventHandler? OnChanged;
        public Action<string, ErrorEventArgs>? OnError;
        public Action<string>? OnExceeded;
    }

    public FileWatcherManager(ILogger<FileWatcherManager> logger, DiagnosticsService diagnostics)
    {
        _logger = logger;
        _diagnostics = diagnostics;
    }

    /// <summary>
    /// Start watching the given folders with watcher settings taken from the provided configuration.
    /// onChanged will be invoked for Created and Changed events.

    /// onError will be invoked when a watcher error occurs. If the restart attempts exceed the configured maximum,
    /// onExceededRestartAttempts will be invoked so the caller can decide what to do (for example, stop the application).
    /// </summary>
    public Task StartWatchingAsync(IEnumerable<string> folders, ExternalConfiguration config, FileSystemEventHandler onChanged, Action<string, ErrorEventArgs>? onError, Action<string>? onExceededRestartAttempts = null)
    {
        foreach (var folder in folders)
        {
            if (string.IsNullOrWhiteSpace(folder)) continue;
            // If folder already being watched, skip.
            if (_watchers.ContainsKey(folder)) continue;
            try
            {
                var watcher = new FileSystemWatcher(folder)
                {
                    InternalBufferSize = Math.Max(4 * 1024, config.FileWatcherInternalBufferSize),
                    IncludeSubdirectories = config.IncludeSubdirectories,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime | NotifyFilters.LastWrite
                };

                if (config.AllowedExtensions.Length > 0)
                {
                    watcher.Filter = "*.*";
                }

                watcher.Created += (s, e) => onChanged?.Invoke(s, e);
                watcher.Changed += (s, e) => onChanged?.Invoke(s, e);
                watcher.Error += (s, e) => HandleWatcherError(folder, e, config, onChanged, onError, onExceededRestartAttempts);
                watcher.EnableRaisingEvents = true;

                if (_watchers.TryAdd(folder, watcher))
                {
                    // store callbacks for testing or later use
                    _folderInfos[folder] = new FolderInfo { Config = config, OnChanged = onChanged, OnError = onError, OnExceeded = onExceededRestartAttempts };
                    _diagnostics.RegisterWatcher(folder);
                    _watchingFolder(_logger, folder, null);
                }
            }
            catch (Exception ex)
            {
                _failedToWatchFolder(_logger, folder, ex);
            }
        }

        return Task.CompletedTask;
    }

    private void HandleWatcherError(string folder, ErrorEventArgs e, ExternalConfiguration config, FileSystemEventHandler onChanged, Action<string, ErrorEventArgs>? onError, Action<string>? onExceeded)
    {
        try
        {
            _watcherError(_logger, folder, e.GetException());
            onError?.Invoke(folder, e);

            var attempts = _restartAttempts.AddOrUpdate(folder, 1, (_, cur) => cur + 1);

            if (attempts <= config.WatcherMaxRestartAttempts)
            {
                _attemptingRestart(_logger, folder, attempts, config.WatcherMaxRestartAttempts, null);
                _ = Task.Run(async () =>
                {
                    await Task.Delay(config.WatcherRestartDelayMilliseconds);
                    await RestartWatcherAsync(folder, config, onChanged, onError, onExceeded);
                });
            }
            else
            {
                _watcherFailedAfterMax(_logger, folder, config.WatcherMaxRestartAttempts, null);
                onExceeded?.Invoke(folder);
            }
        }
        catch (Exception ex)
        {
            _failedHandlingWatcherError(_logger, folder, ex);
        }
    }

    private async Task RestartWatcherAsync(string folder, ExternalConfiguration config, FileSystemEventHandler onChanged, Action<string, ErrorEventArgs>? onError, Action<string>? onExceeded)
    {
        try
        {
            if (_watchers.TryRemove(folder, out var old))
            {
                try { old.EnableRaisingEvents = false; old.Dispose(); } catch { }
                _diagnostics.UnregisterWatcher(folder);
            }

            await StartWatchingAsync(new[] { folder }, config, onChanged, onError, onExceeded);
            _restartAttempts.TryRemove(folder, out _);
            _diagnostics.ResetRestart(folder);
            _watcherRestarted(_logger, folder, null);
        }
        catch (Exception ex)
        {
            _failedRestartWatcher(_logger, folder, ex);
        }
    }

    public Task StopAllAsync()
    {
        var toStop = _watchers.Keys.ToList();
        foreach (var k in toStop)
        {
            if (_watchers.TryRemove(k, out var w))
            {
                try { w.EnableRaisingEvents = false; w.Dispose(); } catch (Exception ex) { _errorStoppingWatcher(_logger, w.Path, ex); }
                _diagnostics.UnregisterWatcher(k);
            }
        }
        return Task.CompletedTask;
    }

    // Register a folder without creating an actual FileSystemWatcher - used by unit tests to exercise restart logic
    internal void RegisterFolderForTest(string folder, ExternalConfiguration config, FileSystemEventHandler onChanged, Action<string, ErrorEventArgs>? onError, Action<string>? onExceeded)
    {
        if (string.IsNullOrWhiteSpace(folder)) return;
        _folderInfos[folder] = new FolderInfo { Config = config, OnChanged = onChanged, OnError = onError, OnExceeded = onExceeded };
    }

    // Simulate an error for the given folder; will invoke the same restart logic as a real watcher error.
    internal Task SimulateWatcherErrorAsync(string folder, Exception ex)
    {
        if (!_folderInfos.TryGetValue(folder, out var info) || info.Config is null)
            return Task.CompletedTask;

        try
        {
            HandleWatcherError(folder, new ErrorEventArgs(ex), info.Config, info.OnChanged ?? ((s, e) => { }), info.OnError, info.OnExceeded);
        }
        catch { }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        try { StopAllAsync().GetAwaiter().GetResult(); } catch { }
    }
}
