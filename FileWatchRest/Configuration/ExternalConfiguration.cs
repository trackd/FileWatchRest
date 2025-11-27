namespace FileWatchRest.Configuration;

/// <summary>
/// Complete configuration stored in AppData as FileWatchRest.json
/// This is now the single source of configuration for the service
/// </summary>
public class ExternalConfiguration {
    /// <summary>
    /// Folders: strongly-typed watched folder configuration. Legacy shapes are migrated on load by ConfigurationService.
    /// </summary>
    public List<WatchedFolderConfig> Folders { get; set; } = [];
    public IEnumerable<string> ValidateFolders() {
        foreach (WatchedFolderConfig folder in Folders) {
            if (string.IsNullOrWhiteSpace(folder.FolderPath)) {
                yield return "FolderPath is required.";
            }

            if (folder.ActionType == FolderActionType.Executable && string.IsNullOrWhiteSpace(folder.ExecutablePath)) {
                yield return $"ExecutablePath is required for folder '{folder.FolderPath}'.";
            }

            if (folder.ActionType == FolderActionType.PowerShellScript && string.IsNullOrWhiteSpace(folder.ScriptPath)) {
                yield return $"ScriptPath is required for folder '{folder.FolderPath}'.";
            }
        }
    }
    public enum FolderActionType {
        RestPost = 0,
        Executable = 1,
        PowerShellScript = 2
    }
    public class WatchedFolderConfig {
        [Required]
        public string FolderPath { get; set; } = string.Empty;
        public FolderActionType ActionType { get; set; } = FolderActionType.RestPost;
        public string? ExecutablePath { get; set; }
        public string? ScriptPath { get; set; }
        public List<string>? Arguments { get; set; }
        public Dictionary<string, string>? AdditionalHeaders { get; set; }
        public override bool Equals(object? obj) => Equals(obj as WatchedFolderConfig);

        public bool Equals(WatchedFolderConfig? other) {
            return other is not null && (ReferenceEquals(this, other) || (string.Equals(FolderPath, other.FolderPath, StringComparison.OrdinalIgnoreCase)
                && ActionType == other.ActionType
                && string.Equals(ExecutablePath, other.ExecutablePath, StringComparison.Ordinal)
                && string.Equals(ScriptPath, other.ScriptPath, StringComparison.Ordinal)
                && ((Arguments is null && other.Arguments is null) || (Arguments is not null && other.Arguments is not null && Arguments.SequenceEqual(other.Arguments)))
                && ((AdditionalHeaders is null && other.AdditionalHeaders is null) || (AdditionalHeaders is not null && other.AdditionalHeaders is not null && AdditionalHeaders.OrderBy(kv => kv.Key).SequenceEqual(other.AdditionalHeaders.OrderBy(kv => kv.Key))))));
        }

        public override int GetHashCode() {
            var h = new HashCode();
            h.Add(FolderPath?.ToLowerInvariant());
            h.Add(ActionType);
            h.Add(ExecutablePath);
            h.Add(ScriptPath);
            if (Arguments is not null) {
                foreach (string a in Arguments) {
                    h.Add(a);
                }
            }
            if (AdditionalHeaders is not null) {
                foreach (KeyValuePair<string, string> kv in AdditionalHeaders.OrderBy(kv => kv.Key)) {
                    h.Add(kv.Key);
                    h.Add(kv.Value);
                }
            }
            return h.ToHashCode();
        }
    }
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

    /// <summary>
    /// Patterns for excluding files from processing. Supports wildcard matching.
    /// Examples: "SAP_*" (starts with SAP_), "*_temp" (ends with _temp), "*backup*" (contains backup).
    /// Files matching any exclude pattern will be ignored even if they pass extension filtering.
    /// </summary>
    public string[] ExcludePatterns { get; set; } = [];

    public bool IncludeSubdirectories { get; set; } = true;
    public int DebounceMilliseconds { get; set; } = 1000;

    /// <summary>
    /// Performance and reliability settings (previously in appsettings.json)
    /// </summary>
    public int Retries { get; set; } = 3;
    public int RetryDelayMilliseconds { get; set; } = 500;
    public int WatcherMaxRestartAttempts { get; set; } = 3;
    public int WatcherRestartDelayMilliseconds { get; set; } = 1000;
    public string DiagnosticsUrlPrefix { get; set; } = "http://localhost:5005/";
    /// <summary>
    /// Optional bearer token required to access diagnostics endpoints (if set).
    /// If null or empty, diagnostics remain open as before.
    /// </summary>
    /// <summary>
    /// Optional bearer token required to access diagnostics endpoints.
    /// If not provided, a random token will be generated when a configuration instance is created.
    /// </summary>
    public string? DiagnosticsBearerToken { get; set; } = Guid.NewGuid().ToString("N");
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

    /// <summary>
    /// If true, zero-byte files will be discarded after waiting for file readiness (see WaitForFileReadyMilliseconds).
    /// Default: false (process zero-byte files if no content arrives within the configured wait time).
    /// </summary>
    public bool DiscardZeroByteFiles { get; set; }

    /// <summary>
    /// Circuit breaker settings (optional)
    /// </summary>
    public bool EnableCircuitBreaker { get; set; }
    /// <summary>
    /// failures before opening
    /// </summary>
    public int CircuitBreakerFailureThreshold { get; set; } = 5;
    /// <summary>
    /// 30s open by default
    /// </summary>
    public int CircuitBreakerOpenDurationMilliseconds { get; set; } = 30_000;

    /// <summary>
    /// Logging configuration (provider-agnostic)
    /// </summary>
    public SimpleFileLoggerOptions Logging { get; set; } = new SimpleFileLoggerOptions();
}

[JsonConverter(typeof(JsonStringEnumConverter<LogType>))]
// Use the generic JsonStringEnumConverter<T> to be compatible with Native AOT
// and avoid SYSLIB1034 warnings about the non-generic converter.
public enum LogType {
    Csv,
    Json,
    Both
}
