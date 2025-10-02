namespace FileWatchRest.Configuration;

/// <summary>
/// Complete configuration stored in AppData as FileWatchRest.json
/// This is now the single source of configuration for the service
/// </summary>
public class ExternalConfiguration
{
    // Core file watching settings
    public string[] Folders { get; set; } = [];
    public string? ApiEndpoint { get; set; }

    /// <summary>
    /// Bearer token for API authentication.
    /// This is automatically encrypted using machine-specific encryption when saved.
    /// Plain text tokens are automatically encrypted on first save.
    /// </summary>
    public string? BearerToken { get; set; }
    public bool PostFileContents { get; set; }
    public string ProcessedFolder { get; set; } = "processed";
    public bool MoveProcessedFiles { get; set; }
    public string[] AllowedExtensions { get; set; } = [];
    public bool IncludeSubdirectories { get; set; } = true;
    public int DebounceMilliseconds { get; set; } = 1000;

    // Performance and reliability settings (previously in appsettings.json)
    public int Retries { get; set; } = 3;
    public int RetryDelayMilliseconds { get; set; } = 500;
    public int WatcherMaxRestartAttempts { get; set; } = 3;
    public int WatcherRestartDelayMilliseconds { get; set; } = 1000;
    public string DiagnosticsUrlPrefix { get; set; } = "http://localhost:5005/";
    public int ChannelCapacity { get; set; } = 1000;
    public int MaxParallelSends { get; set; } = 4;
    public int FileWatcherInternalBufferSize { get; set; } = 64 * 1024;
    public int WaitForFileReadyMilliseconds { get; set; }

    /// <summary>
    /// Maximum number of bytes to include when reading file contents for posting.
    /// Files larger than this will be sent without their content to avoid large memory allocations.
    /// Set to 0 to disable content posting regardless of PostFileContents.
    /// </summary>
    public long MaxContentBytes { get; set; } = 5 * 1024 * 1024; // 5 MB default

    /// <summary>
    /// Threshold in bytes above which the service will stream the file content instead of including it inline in JSON.
    /// Set to 0 to disable streaming and always send as JSON when PostFileContents is enabled.
    /// </summary>
    public long StreamingThresholdBytes { get; set; } = 256 * 1024; // 256 KB default

    // Circuit breaker settings (optional)
    public bool EnableCircuitBreaker { get; set; }
    public int CircuitBreakerFailureThreshold { get; set; } = 5; // failures before opening
    public int CircuitBreakerOpenDurationMilliseconds { get; set; } = 30_000; // 30s open by default

    // Logging configuration (provider-agnostic)
    public LoggingOptions Logging { get; set; } = new LoggingOptions();
}

public class LoggingOptions
{
    /// <summary>
    /// New unified LogType setting; defaults to CSV output.
    /// </summary>
    public LogType LogType { get; set; } = LogType.Csv;

    /// <summary>
    /// Single file name/pattern used for both CSV/JSON outputs; provider will append extension when necessary.
    /// </summary>
    public string FilePathPattern { get; set; } = "logs/FileWatchRest_{0:yyyyMMdd_HHmmss}";

    // Legacy properties retained for backward compatibility with existing configuration files
    public bool UseJsonFile { get; set; } = false; // JSON opt-in
    public string JsonFilePath { get; set; } = "logs/FileWatchRest_{0:yyyyMMdd_HHmmss}.json";
    public bool UseCsvFile { get; set; } = true;
    public string CsvFilePath { get; set; } = "logs/FileWatchRest_{0:yyyyMMdd_HHmmss}.csv";

    // Canonical log level for the logging subsystem. Use string to preserve JSON readability and avoid coupling to Microsoft types in the configuration model.
    public string LogLevel { get; set; } = "Information";

    public int RetainedFileCountLimit { get; set; } = 14;
}

public enum LogType
{
    Csv,
    Json,
    Both
}
