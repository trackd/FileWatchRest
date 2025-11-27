namespace FileWatchRest.Services;

/// <summary>
/// Manages a set of <see cref="FileSystemWatcher"/> instances for one or more folders, including restart attempts on error.
/// The manager wraps the OS watchers and provides restart logic and callback hooks for file events and errors.
/// </summary>
/// <param name="logger"></param>
/// <param name="diagnostics"></param>
public class FileWatcherManager(ILogger<FileWatcherManager> logger, DiagnosticsService diagnostics) : IFileWatcherManager {

    /// <summary>
    /// Constructor for Moq compatibility.
    /// </summary>
    // Maps folder path to list of actions
    private readonly Dictionary<string, List<IFolderAction>> _folderActions = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Rebuilds the folder-to-action mapping from configuration. Call on config reload.
    /// </summary>
    /// <param name="configs"></param>
    /// <param name="worker"></param>
    public void ConfigureFolderActions(List<ExternalConfiguration.WatchedFolderConfig> configs, ExternalConfiguration globalConfig, Worker worker) {
        _folderActions.Clear();
        foreach (ExternalConfiguration.WatchedFolderConfig config in configs) {
            var actions = new List<IFolderAction>();

            // Resolve action by name and build action instances from the named ActionConfig.
            ExternalConfiguration.ActionConfig? actionDef = null;
            if (!string.IsNullOrWhiteSpace(config.ActionName) && globalConfig is not null) {
                actionDef = globalConfig.Actions?.FirstOrDefault(a => string.Equals(a.Name, config.ActionName, StringComparison.OrdinalIgnoreCase));
            }

            if (actionDef is not null) {
                switch (actionDef.ActionType) {
                    case ExternalConfiguration.FolderActionType.RestPost:
                        actions.Add(new RestPostAction(worker));
                        break;
                    case ExternalConfiguration.FolderActionType.PowerShellScript:
                        if (!string.IsNullOrWhiteSpace(actionDef.ScriptPath)) {
                            actions.Add(new PowerShellScriptAction(actionDef.ScriptPath, actionDef.Arguments));
                        }
                        break;
                    case ExternalConfiguration.FolderActionType.Executable:
                        if (!string.IsNullOrWhiteSpace(actionDef.ExecutablePath)) {
                            actions.Add(new ExecutableAction(actionDef.ExecutablePath, actionDef.Arguments));
                        }
                        break;
                    default:
                        break;
                }
            }

            if (!string.IsNullOrWhiteSpace(config.FolderPath)) {
                _folderActions[config.FolderPath] = actions;
            }
        }
    }
    // Back-compat overload used by some tests/callers: delegate to new API with empty global config
    public void ConfigureFolderActions(List<ExternalConfiguration.WatchedFolderConfig> configs, Worker worker) => ConfigureFolderActions(configs, new ExternalConfiguration(), worker);
    private readonly ILogger<FileWatcherManager> _logger = logger;
    private readonly DiagnosticsService _diagnostics = diagnostics;
    private readonly ConcurrentDictionary<string, FileSystemWatcher> _watchers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, int> _restartAttempts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Internal folder metadata for watcher management
    /// </summary>
    private readonly ConcurrentDictionary<string, FolderInfo> _folderInfos = new(StringComparer.OrdinalIgnoreCase);

    internal sealed class FolderInfo {
        public ExternalConfiguration? Config;
        public FileSystemEventHandler? OnChanged;
        public Action<string, ErrorEventArgs>? OnError;
        public Action<string>? OnExceeded;
    }

    /// <summary>
    /// Start watching the given folders with watcher settings taken from the provided configuration.
    /// onChanged will be invoked for Created and Changed events. onError will be invoked when a watcher error occurs. If the restart attempts exceed the configured maximum, onExceededRestartAttempts will be invoked so the caller can decide what to do (for example, stop the application).
    /// </summary>
    /// <param name="folders"></param>
    /// <param name="config"></param>
    /// <param name="onChanged"></param>
    /// <param name="onError"></param>
    /// <param name="onExceededRestartAttempts"></param>
    // Back-compatible overload: accept simple folder paths and global config (creates default watched-folder configs)
    public Task StartWatchingAsync(IEnumerable<string> folders, ExternalConfiguration config, FileSystemEventHandler onChanged, Action<string, ErrorEventArgs>? onError, Action<string>? onExceededRestartAttempts = null) {
        var list = new List<ExternalConfiguration.WatchedFolderConfig>();
        foreach (string f in folders) {
            list.Add(new ExternalConfiguration.WatchedFolderConfig { FolderPath = f });
        }

        return StartWatchingAsync(list, config, onChanged, onError, onExceededRestartAttempts);
    }

    public Task StartWatchingAsync(IEnumerable<ExternalConfiguration.WatchedFolderConfig> folders, ExternalConfiguration globalConfig, FileSystemEventHandler onChanged, Action<string, ErrorEventArgs>? onError, Action<string>? onExceededRestartAttempts = null) {
        foreach (ExternalConfiguration.WatchedFolderConfig folderCfg in folders) {
            string folder = folderCfg.FolderPath;
            if (string.IsNullOrWhiteSpace(folder) || _watchers.ContainsKey(folder)) {
                continue;
            }

            try {
                // Resolve action to get watcher-level settings (IncludeSubdirectories)
                bool includeSubdirs = globalConfig.IncludeSubdirectories;
                if (!string.IsNullOrWhiteSpace(folderCfg.ActionName) && globalConfig.Actions is not null) {
                    ExternalConfiguration.ActionConfig? action = globalConfig.Actions.FirstOrDefault(a =>
                        string.Equals(a.Name, folderCfg.ActionName, StringComparison.OrdinalIgnoreCase));
                    if (action is not null) {
                        includeSubdirs = action.IncludeSubdirectories ?? globalConfig.IncludeSubdirectories;
                    }
                }

                var watcher = new FileSystemWatcher(folder) {
                    InternalBufferSize = Math.Max(4 * 1024, globalConfig.FileWatcherInternalBufferSize),
                    IncludeSubdirectories = includeSubdirs,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime | NotifyFilters.LastWrite,
                    Filter = "*.*"
                };

                watcher.Created += (s, e) => HandleFileEvent(folder, e);
                watcher.Changed += (s, e) => HandleFileEvent(folder, e);
                watcher.Renamed += (s, e) => HandleFileEvent(folder, e);
                watcher.Error += (s, e) => HandleWatcherError(folder, e, onChanged, onError, onExceededRestartAttempts);
                watcher.EnableRaisingEvents = true;

                if (_watchers.TryAdd(folder, watcher)) {
                    _folderInfos[folder] = new FolderInfo { Config = globalConfig, OnChanged = onChanged, OnError = onError, OnExceeded = onExceededRestartAttempts };
                    _diagnostics.RegisterWatcher(folder);
                    LoggerDelegates.WatchingFolder(_logger, folder, null);
                }
            }
            catch (Exception ex) {
                LoggerDelegates.FailedToWatchFolder(_logger, folder, ex);
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Handles file system events and executes mapped actions.
    /// </summary>
    /// <param name="folder"></param>
    /// <param name="e"></param>
    private void HandleFileEvent(string folder, FileSystemEventArgs e) {
        // Invoke the registered OnChanged handler for this folder (used by tests and consumers)
        if (_folderInfos.TryGetValue(folder, out FolderInfo? info) && info.OnChanged is not null) {
            info.OnChanged.Invoke(this, e);
        }

        // Execute mapped actions for this folder (if any). Actions should use
        // the Worker's debounced enqueue API so they do not directly invoke processing
        // and thereby avoid duplicate immediate posts when multiple watcher events fire.
        if (_folderActions.TryGetValue(folder, out List<IFolderAction>? actions) && actions.Count > 0) {
            var fileEvent = new FileEventRecord { Path = e.FullPath };
            foreach (IFolderAction action in actions) {
                _ = action.ExecuteAsync(fileEvent, CancellationToken.None);
            }
        }
    }

    /// <summary>
    /// Handles file system watcher errors, attempts restart, and invokes error callbacks.
    /// </summary>
    /// <param name="folder"></param>
    /// <param name="e"></param>
    /// <param name="config"></param>
    /// <param name="onChanged"></param>
    /// <param name="onError"></param>
    /// <param name="onExceeded"></param>
    private void HandleWatcherError(string folder, ErrorEventArgs e, FileSystemEventHandler onChanged, Action<string, ErrorEventArgs>? onError, Action<string>? onExceeded) {
        try {
            LoggerDelegates.WatcherError(_logger, folder, e.GetException());
            onError?.Invoke(folder, e);

            int attempts = _restartAttempts.AddOrUpdate(folder, 1, (_, cur) => cur + 1);

            ExternalConfiguration? cfg = null;
            if (_folderInfos.TryGetValue(folder, out FolderInfo? info)) { cfg = info.Config; }

            int maxAttempts = cfg?.WatcherMaxRestartAttempts ?? 3;
            int restartDelay = cfg?.WatcherRestartDelayMilliseconds ?? 1000;

            if (attempts <= maxAttempts) {
                LoggerDelegates.AttemptingRestart(_logger, folder, attempts, maxAttempts, null);
                _ = Task.Run(async () => {
                    await Task.Delay(restartDelay);
                    await RestartWatcherAsync(folder, onChanged, onError, onExceeded);
                });
            }
            else {
                LoggerDelegates.WatcherFailedAfterMax(_logger, folder, maxAttempts, null);
                onExceeded?.Invoke(folder);
            }
        }
        catch (Exception ex) {
            LoggerDelegates.FailedHandlingWatcherError(_logger, folder, ex);
        }
    }

    /// <summary>
    /// Restarts a file system watcher for the specified folder.
    /// </summary>
    /// <param name="folder"></param>
    /// <param name="config"></param>
    /// <param name="onChanged"></param>
    /// <param name="onError"></param>
    /// <param name="onExceeded"></param>
    private async Task RestartWatcherAsync(string folder, FileSystemEventHandler onChanged, Action<string, ErrorEventArgs>? onError, Action<string>? onExceeded) {
        try {
            if (_watchers.TryRemove(folder, out FileSystemWatcher? old)) {
                try { old.EnableRaisingEvents = false; old.Dispose(); } catch { }
                _diagnostics.UnregisterWatcher(folder);
            }

            // Restart using the stored folder-specific config if available
            ExternalConfiguration? cfg = null;
            if (_folderInfos.TryGetValue(folder, out FolderInfo? info) && info.Config is not null) {
                cfg = info.Config;
            }

            if (cfg is not null) {
                // Start a watcher for this single folder using the stored runtime config
                var singleFolder = new ExternalConfiguration.WatchedFolderConfig { FolderPath = folder };
                await StartWatchingAsync([singleFolder], cfg, onChanged, onError, onExceeded);
            }
            else {
                // Fallback: start with a minimal config using defaults
                var singleFolder = new ExternalConfiguration.WatchedFolderConfig { FolderPath = folder };
                await StartWatchingAsync([singleFolder], new ExternalConfiguration(), onChanged, onError, onExceeded);
            }
            _restartAttempts.TryRemove(folder, out _);
            _diagnostics.ResetRestart(folder);
            LoggerDelegates.WatcherRestarted(_logger, folder, null);
        }
        catch (Exception ex) {
            LoggerDelegates.FailedRestartWatcher(_logger, folder, ex);
        }
    }

    /// <summary>
    /// Stops all active watchers and unregisters them from diagnostics.
    /// </summary>
    public Task StopAllAsync() {
        var toStop = _watchers.Keys.ToList();
        foreach (string? k in toStop) {
            if (_watchers.TryRemove(k, out FileSystemWatcher? w)) {
                try { w.EnableRaisingEvents = false; w.Dispose(); } catch (Exception ex) { LoggerDelegates.ErrorStoppingWatcher(_logger, w.Path, ex); }
                _diagnostics.UnregisterWatcher(k);
            }
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Disposes all watchers and releases resources.
    /// </summary>
    public void Dispose() {
        try { StopAllAsync().GetAwaiter().GetResult(); } catch { }
        GC.SuppressFinalize(this);
    }
}
