namespace FileWatchRest.Services;

/// <summary>
/// Provides diagnostics and metrics for file watcher and notification operations.
/// </summary>
public interface IDiagnosticsService : IDisposable {
    /// <summary>
    /// Returns true if the file at the given path has been posted and acknowledged (HTTP 200).
    /// </summary>
    /// <param name="path"></param>
    bool IsFilePosted(string path);

    /// <summary>
    /// Record a file processing event (success or failure).
    /// </summary>
    /// <param name="path"></param>
    /// <param name="success"></param>
    /// <param name="statusCode"></param>
    void RecordFileEvent(string path, bool success, int? statusCode);

    /// <summary>
    /// Register an active watcher for a folder.
    /// </summary>
    /// <param name="folder"></param>
    void RegisterWatcher(string folder);

    /// <summary>
    /// Unregister an active watcher for a folder.
    /// </summary>
    /// <param name="folder"></param>
    void UnregisterWatcher(string folder);

    /// <summary>
    /// Reset restart attempts counter for a folder.
    /// </summary>
    /// <param name="folder"></param>
    void ResetRestart(string folder);

    /// <summary>
    /// Update circuit breaker state for diagnostics tracking.
    /// </summary>
    /// <param name="endpoint"></param>
    /// <param name="failures"></param>
    /// <param name="openUntil"></param>
    void UpdateCircuitState(string endpoint, int failures, DateTimeOffset? openUntil);

    /// <summary>
    /// Register a resilience metrics provider for aggregated metrics.
    /// </summary>
    /// <param name="provider"></param>
    void RegisterResilienceMetricsProvider(IResilienceMetricsProvider provider);

    /// <summary>
    /// Start the diagnostics HTTP server.
    /// </summary>
    /// <param name="urlPrefix"></param>
    void StartHttpServer(string urlPrefix);

    /// <summary>
    /// Restart the diagnostics HTTP server with a new URL prefix.
    /// </summary>
    /// <param name="urlPrefix"></param>
    void RestartHttpServer(string urlPrefix);

    /// <summary>
    /// Set the bearer token for HTTP server authentication.
    /// </summary>
    /// <param name="token"></param>
    void SetBearerToken(string? token);

    /// <summary>
    /// Set the live runtime configuration for diagnostics display.
    /// </summary>
    /// <param name="cfg"></param>
    void SetConfiguration(ExternalConfiguration? cfg);
}
