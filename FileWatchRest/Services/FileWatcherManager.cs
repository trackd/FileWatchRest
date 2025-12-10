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
    // Maps folder path to a single resolved action instance (config model supports one action per folder)
    internal readonly Dictionary<string, IFolderAction?> _folderActions = new(StringComparer.OrdinalIgnoreCase);
    // Maps folder path to resolved ActionType (if any)
    internal readonly Dictionary<string, ExternalConfiguration.FolderActionType?> _folderActionTypes = new(StringComparer.OrdinalIgnoreCase);
    // Maps folder path to the configured ActionName (if any) so we can resolve ActionConfig from a global config later
    internal readonly Dictionary<string, string?> _folderActionNames = new(StringComparer.OrdinalIgnoreCase);
    // (normalized full paths cache declared later)

    /// <summary>
    /// Rebuilds the folder-to-action mapping from configuration. Call on config reload.
    /// </summary>
    /// <param name="configs"></param>
    /// <param name="worker"></param>
    public void ConfigureFolderActions(List<ExternalConfiguration.WatchedFolderConfig> configs, ExternalConfiguration globalConfig, Worker worker) {
        _folderActions.Clear();
        _folderActionTypes.Clear();
        _folderActionNames.Clear();
        foreach (ExternalConfiguration.WatchedFolderConfig config in configs) {
            IFolderAction? actionInstance = null;

            // Resolve action by name and build action instances from the named ActionConfig.
            ExternalConfiguration.ActionConfig? actionDef = null;
            if (!string.IsNullOrWhiteSpace(config.ActionName) && globalConfig is not null) {
                actionDef = globalConfig.Actions?.FirstOrDefault(a => string.Equals(a.Name, config.ActionName, StringComparison.OrdinalIgnoreCase));
            }

            if (actionDef is not null) {
                switch (actionDef.ActionType) {
                    case ExternalConfiguration.FolderActionType.RestPost:
                        actionInstance = new RestPostAction(worker);
                        break;
                    case ExternalConfiguration.FolderActionType.PowerShellScript:
                        if (!string.IsNullOrWhiteSpace(actionDef.ScriptPath)) {
                            actionInstance = new PowerShellScriptAction(actionDef.ScriptPath, actionDef.Arguments, null, actionDef.ExecutionTimeoutMilliseconds, actionDef.IgnoreOutput ?? false);
                        }
                        else {
                            LoggerDelegates.FolderActionScriptMissing(_logger, config.FolderPath ?? string.Empty, string.Empty, null);
                        }
                        break;
                    case ExternalConfiguration.FolderActionType.Executable:
                        if (!string.IsNullOrWhiteSpace(actionDef.ExecutablePath)) {
                            actionInstance = new ExecutableAction(actionDef.ExecutablePath, actionDef.Arguments, null, actionDef.ExecutionTimeoutMilliseconds, actionDef.IgnoreOutput ?? false);
                        }
                        else {
                            LoggerDelegates.FolderActionExecutableMissing(_logger, config.FolderPath ?? string.Empty, string.Empty, null);
                        }
                        break;
                    default:
                        break;
                }
            }

            if (!string.IsNullOrWhiteSpace(config.FolderPath)) {
                _folderActions[config.FolderPath] = actionInstance;
                _folderActionTypes[config.FolderPath] = actionDef?.ActionType;
                _folderActionNames[config.FolderPath] = string.IsNullOrWhiteSpace(config.ActionName) ? null : config.ActionName;
                try {
                    string full = Path.GetFullPath(config.FolderPath);
                    _normalizedFolderFullPaths[config.FolderPath] = full;
                }
                catch {
                    // ignore normalization errors
                }
            }
        }
    }
    public ExternalConfiguration.FolderActionType? GetActionTypeForPath(string path) {
        if (string.IsNullOrWhiteSpace(path)) return null;
        string normalized = Path.GetFullPath(path);

        // Find longest-prefix matching configured folder
        string? bestMatch = null;
        foreach (string folder in _folderActionTypes.Keys) {
            if (string.IsNullOrWhiteSpace(folder)) continue;
            string folderFull;
            try { folderFull = Path.GetFullPath(folder); } catch { continue; }
            if (!normalized.StartsWith(folderFull, StringComparison.OrdinalIgnoreCase)) continue;
            if (bestMatch is null || folderFull.Length > Path.GetFullPath(bestMatch).Length) bestMatch = folder;
        }

        return bestMatch is null
            ? null
            : _folderActionTypes.TryGetValue(bestMatch, out ExternalConfiguration.FolderActionType? actionType) ? actionType : null;
    }
    /// <summary>
    /// Try to resolve the configured folder context (merged runtime config and actions)
    /// for the provided file path by performing a longest-prefix match against configured folders.
    /// Returns true when a matching configured folder is found.
    /// </summary>
    public bool TryGetFolderContextForPath(string path, ExternalConfiguration globalConfig, out ExternalConfiguration mergedCfg, out IFolderAction? action, out string? matchedFolder) {
        mergedCfg = globalConfig; action = null; matchedFolder = null;
        if (string.IsNullOrWhiteSpace(path)) return false;
        string normalized;
        try { normalized = Path.GetFullPath(path); } catch { return false; }

        string? bestKey = null;
        string? bestFull = null;

        foreach (KeyValuePair<string, string> kv in _normalizedFolderFullPaths) {
            string key = kv.Key;
            string full = kv.Value;
            if (string.IsNullOrWhiteSpace(full)) continue;
            if (!normalized.StartsWith(full, StringComparison.OrdinalIgnoreCase)) continue;
            if (bestFull is null || full.Length > bestFull.Length) {
                bestFull = full;
                bestKey = key;
            }
        }

        if (bestKey is null) return false;

        matchedFolder = bestKey;
        _folderInfos.TryGetValue(bestKey, out FolderInfo? info);
        // Prefer the runtime-stored merged config if available
        if (info?.Config is not null) {
            mergedCfg = info.Config;
        }
        else {
            // Attempt to construct a merged runtime config from the provided global config and the configured action name
            // mergedCfg = globalConfig ?? new ExternalConfiguration();
            if (_folderActionNames.TryGetValue(bestKey, out string? actionName) && !string.IsNullOrWhiteSpace(actionName) && mergedCfg.Actions is not null) {
                ExternalConfiguration.ActionConfig? actionDef = mergedCfg.Actions.FirstOrDefault(a => string.Equals(a.Name, actionName, StringComparison.OrdinalIgnoreCase));
                if (actionDef is not null) {
                    mergedCfg = ExternalConfiguration.MergeWithAction(mergedCfg, actionDef);
                }
            }
        }

        _folderActions.TryGetValue(bestKey, out IFolderAction? act);
        action = act;
        return true;
    }
    // Back-compat overload used by some tests/callers: delegate to new API with empty global config
    public void ConfigureFolderActions(List<ExternalConfiguration.WatchedFolderConfig> configs, Worker worker) => ConfigureFolderActions(configs, new ExternalConfiguration(), worker);
    private readonly ILogger<FileWatcherManager> _logger = logger;
    private readonly DiagnosticsService _diagnostics = diagnostics;
    private readonly ConcurrentDictionary<string, List<FileSystemWatcher>> _watchers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, int> _restartAttempts = new(StringComparer.OrdinalIgnoreCase);
    // Cache of normalized full paths for configured folders to speed up longest-prefix matching
    private readonly ConcurrentDictionary<string, string> _normalizedFolderFullPaths = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Internal folder metadata for watcher management
    /// </summary>
    private readonly ConcurrentDictionary<string, FolderInfo> _folderInfos = new(StringComparer.OrdinalIgnoreCase);

    internal sealed class FolderInfo {
        public ExternalConfiguration? Config;
        public FileSystemEventHandler? OnChanged;
        public Action<string, ErrorEventArgs>? OnError;
        public Action<string>? OnExceeded;
        public IFolderAction? Action;
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
                // Resolve action once and use the central merge helper to obtain the effective config
                ExternalConfiguration.ActionConfig? action = null;
                if (!string.IsNullOrWhiteSpace(folderCfg.ActionName) && globalConfig.Actions is not null) {
                    action = globalConfig.Actions.FirstOrDefault(a => string.Equals(a.Name, folderCfg.ActionName, StringComparison.OrdinalIgnoreCase));
                }

                var mergedCfg = ExternalConfiguration.MergeWithAction(globalConfig, action);
                bool includeSubdirs = mergedCfg.IncludeSubdirectories;
                string[] allowed = mergedCfg.AllowedExtensions;

                // Resolve the configured action instance for this folder (captured as a snapshot)
                IFolderAction? actionSnapshot = null;
                if (_folderActions.TryGetValue(folder, out IFolderAction? existingAction)) actionSnapshot = existingAction;

                var watchers = new List<FileSystemWatcher>();

                // If specific allowed extensions are configured, create one watcher per extension pattern
                if (allowed is { Length: > 0 }) {
                    foreach (string ext in allowed) {
                        string pattern = (ext ?? string.Empty).Trim();
                        if (pattern.Length == 0) continue;
                        // If pattern already contains wildcards, leave as-is. Otherwise:
                        // - ".ext" -> "*.ext"
                        // - "ext"  -> "*.ext"
                        // - "name.ext" -> exact filename (keep as-is)
                        if (!pattern.Contains('*') && !pattern.Contains('?')) {
                            if (pattern.StartsWith('.')) pattern = "*" + pattern;
                            else if (!pattern.Contains('.')) pattern = "*." + pattern;
                        }

                        var w = new FileSystemWatcher(folder) {
                            InternalBufferSize = Math.Max(4 * 1024, mergedCfg.FileWatcherInternalBufferSize),
                            IncludeSubdirectories = includeSubdirs,
                            NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime | NotifyFilters.LastWrite,
                            Filter = pattern
                        };
                        w.Created += (s, e) => HandleFileEvent(folder, e);
                        w.Changed += (s, e) => HandleFileEvent(folder, e);
                        w.Renamed += (s, e) => HandleFileEvent(folder, e);
                        w.Error += (s, e) => HandleWatcherError(folder, e, onChanged, onError, onExceededRestartAttempts);
                        w.EnableRaisingEvents = true;
                        watchers.Add(w);
                    }
                }
                else {
                    var w = new FileSystemWatcher(folder) {
                        InternalBufferSize = Math.Max(4 * 1024, mergedCfg.FileWatcherInternalBufferSize),
                        IncludeSubdirectories = includeSubdirs,
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime | NotifyFilters.LastWrite,
                        // Empty string watches all files (preferred to "*.*").
                        Filter = string.Empty
                    };
                    w.Created += (s, e) => HandleFileEvent(folder, e);
                    w.Changed += (s, e) => HandleFileEvent(folder, e);
                    w.Renamed += (s, e) => HandleFileEvent(folder, e);
                    w.Error += (s, e) => HandleWatcherError(folder, e, onChanged, onError, onExceededRestartAttempts);
                    w.EnableRaisingEvents = true;
                    watchers.Add(w);
                }

                if (_watchers.TryAdd(folder, watchers)) {
                    _folderInfos[folder] = new FolderInfo { Config = mergedCfg, Action = actionSnapshot, OnChanged = onChanged, OnError = onError, OnExceeded = onExceededRestartAttempts };
                    try { _normalizedFolderFullPaths[folder] = Path.GetFullPath(folder); } catch { }
                    _diagnostics.RegisterWatcher(folder);
                    // Emit both the simple folder start and a detailed configuration event.
                    LoggerDelegates.WatchingFolder(_logger, folder, null);
                    string filters = (allowed is { Length: > 0 }) ? string.Join(",", allowed) : "*.*";
                    LoggerDelegates.WatchingFolderConfigured(_logger, folder, filters, mergedCfg.IncludeSubdirectories, mergedCfg.FileWatcherInternalBufferSize, null);
                }
                else {
                    // If we failed to add, dispose created watchers
                    foreach (FileSystemWatcher w in watchers) { try { w.EnableRaisingEvents = false; w.Dispose(); } catch { } }
                }
            }
            catch (UnauthorizedAccessException uex) {
                LoggerDelegates.WatcherAccessDenied(_logger, folder, uex);
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
    internal void HandleFileEvent(string folder, FileSystemEventArgs e) {
        // Invoke the registered OnChanged handler for this folder (used by tests and consumers)
        if (_folderInfos.TryGetValue(folder, out FolderInfo? info) && info.OnChanged is not null) {
            info.OnChanged.Invoke(this, e);
        }

        // For backward compatibility, invoke the configured folder action asynchronously
        // so callers that expect immediate execution (tests/consumers) continue to work.
        // The Worker still performs debounced processing for coordinated posting.
        if (info is not null && info.Action is not null) {
            try {
                _ = Task.Run(async () => {
                    try { await info.Action.ExecuteAsync(new FileEventRecord { Path = e.FullPath }, CancellationToken.None); } catch { }
                });
            }
            catch { /* best-effort */ }
        }
        else {
            // Back-compat: if no FolderInfo exists (tests or manual mapping), look up configured action map
            if (_folderActions.TryGetValue(folder, out IFolderAction? fallback) && fallback is not null) {
                try {
                    _ = Task.Run(async () => {
                        try { await fallback.ExecuteAsync(new FileEventRecord { Path = e.FullPath }, CancellationToken.None); } catch { }
                    });
                }
                catch { /* best-effort */ }
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
            if (_watchers.TryRemove(folder, out List<FileSystemWatcher>? oldList)) {
                foreach (FileSystemWatcher old in oldList) {
                    try { old.EnableRaisingEvents = false; old.Dispose(); } catch { }
                }
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
        foreach (string k in toStop) {
            if (_watchers.TryRemove(k, out List<FileSystemWatcher>? list)) {
                foreach (FileSystemWatcher w in list) {
                    try { w.EnableRaisingEvents = false; w.Dispose(); } catch (Exception ex) { LoggerDelegates.ErrorStoppingWatcher(_logger, w?.Path ?? k ?? string.Empty, ex); }
                }
                if (!string.IsNullOrEmpty(k)) _diagnostics.UnregisterWatcher(k);
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
