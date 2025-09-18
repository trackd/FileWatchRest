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
    public bool PostFileContents { get; set; } = false;
    public string ProcessedFolder { get; set; } = "processed";
    public bool MoveProcessedFiles { get; set; } = false;
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
    public int WaitForFileReadyMilliseconds { get; set; } = 0;

    // Logging configuration
    /// <summary>
    /// Logging level for the application. Valid values: Trace, Debug, Information, Warning, Error, Critical, None
    /// Default: Information
    /// </summary>
    public string LogLevel { get; set; } = "Information";
}
