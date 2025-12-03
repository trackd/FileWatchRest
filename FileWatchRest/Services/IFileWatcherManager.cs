namespace FileWatchRest.Services;

/// <summary>
/// Manages file system watchers for monitoring folders.
/// </summary>
public interface IFileWatcherManager : IDisposable {
    /// <summary>
    /// Start watching the given folders with settings from configuration.
    /// </summary>
    /// <param name="folders">Folders to watch</param>
    /// <param name="config">Configuration settings</param>
    /// <param name="onChanged">Callback for file changes</param>
    /// <param name="onError">Callback for watcher errors</param>
    /// <param name="onExceededRestartAttempts">Callback when restart attempts exceeded</param>
    Task StartWatchingAsync(
        IEnumerable<ExternalConfiguration.WatchedFolderConfig> folders,
        ExternalConfiguration globalConfig,
        FileSystemEventHandler onChanged,
        Action<string, ErrorEventArgs>? onError,
        Action<string>? onExceededRestartAttempts = null);

    /// <summary>
    /// Stop all active watchers.
    /// </summary>
    Task StopAllAsync();

    /// <summary>
    /// Configure folder-specific actions based on configuration.
    /// </summary>
    /// <param name="configs"></param>
    /// <param name="worker"></param>
    void ConfigureFolderActions(List<ExternalConfiguration.WatchedFolderConfig> configs, ExternalConfiguration globalConfig, Worker worker);

    /// <summary>
    /// Determine the configured action type for a given file path by matching the most specific watched folder.
    /// Returns null when no matching folder or action is found.
    /// </summary>
    ExternalConfiguration.FolderActionType? GetActionTypeForPath(string path);
}
