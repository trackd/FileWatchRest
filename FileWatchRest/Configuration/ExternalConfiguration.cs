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

            if (string.IsNullOrWhiteSpace(folder.ActionName)) {
                yield return $"ActionName is required for folder '{folder.FolderPath}'.";
            }
            else {
                /// Verify the referenced action exists
                if (Actions?.Any(a => string.Equals(a.Name, folder.ActionName, StringComparison.OrdinalIgnoreCase)) != true) {
                    yield return $"Folder '{folder.FolderPath}' references action '{folder.ActionName}' which does not exist in Actions list.";
                }
            }
        }
    }

    public IEnumerable<string> ValidateActions() {
        if (Actions is null || Actions.Count == 0) {
            yield return "At least one Action must be defined in Actions list.";
            yield break;
        }

        var actionNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ActionConfig action in Actions) {
            if (string.IsNullOrWhiteSpace(action.Name)) {
                yield return "Action Name is required.";
            }
            else if (!actionNames.Add(action.Name)) {
                yield return $"Duplicate action name '{action.Name}' found.";
            }

            switch (action.ActionType) {
                case FolderActionType.RestPost:
                    if (string.IsNullOrWhiteSpace(action.ApiEndpoint)) {
                        yield return $"ApiEndpoint is required for REST action '{action.Name}'.";
                    }
                    break;
                case FolderActionType.PowerShellScript:
                    if (string.IsNullOrWhiteSpace(action.ScriptPath)) {
                        yield return $"ScriptPath is required for PowerShell action '{action.Name}'.";
                    }
                    break;
                case FolderActionType.Executable:
                    if (string.IsNullOrWhiteSpace(action.ExecutablePath)) {
                        yield return $"ExecutablePath is required for Executable action '{action.Name}'.";
                    }
                    break;
                default:
                    break;
            }
        }
    }
    [JsonConverter(typeof(JsonStringEnumConverter<FolderActionType>))]
    public enum FolderActionType {
        RestPost = 0,
        Executable = 1,
        PowerShellScript = 2
    }
    public class WatchedFolderConfig {
        [Required]
        public string FolderPath { get; set; } = string.Empty;
        /// <summary>
        /// Required reference to a named action defined in Actions[].
        /// The action defines all processing behavior for files in this folder.
        /// </summary>
        [Required]
        public string ActionName { get; set; } = string.Empty;

        // No per-folder overrides: all behavior comes from the referenced action.

        /// <summary>
        /// Legacy migration properties removed: use `ActionName` and `Actions[]` instead.
        /// </summary>
        /// <param name="obj"></param>
        public override bool Equals(object? obj) => Equals(obj as WatchedFolderConfig);

        public bool Equals(WatchedFolderConfig? other) {
            return other is not null && (ReferenceEquals(this, other) ||
                (string.Equals(FolderPath, other.FolderPath, StringComparison.OrdinalIgnoreCase)
                && string.Equals(ActionName, other.ActionName, StringComparison.OrdinalIgnoreCase)));
        }

        public override int GetHashCode() {
            var h = new HashCode();
            h.Add(FolderPath?.ToLowerInvariant());
            h.Add(ActionName?.ToLowerInvariant());
            return h.ToHashCode();
        }
    }
    /// <summary>
    /// Named action definitions. Each action is a complete, reusable processing configuration.
    /// Settings defined here override global defaults. Folders reference actions by Name.
    /// </summary>
    public class ActionConfig {
        [Required]
        public string Name { get; set; } = string.Empty;

        [Required]
        public FolderActionType ActionType { get; set; } = FolderActionType.RestPost;

        /// <summary>
        /// Action-specific execution settings
        /// </summary>
        public string? ExecutablePath { get; set; }
        public string? ScriptPath { get; set; }
        public List<string>? Arguments { get; set; }
        public Dictionary<string, string>? AdditionalHeaders { get; set; }

        /// <summary>
        /// REST API settings (for RestPost actions)
        /// </summary>
        public string? ApiEndpoint { get; set; }
        /// <summary>
        /// Bearer token for this action's API. Encrypted tokens are supported.
        /// </summary>
        public string? BearerToken { get; set; }

        /// <summary>
        /// File processing behavior (overrides global defaults when set)
        /// </summary>
        public bool? PostFileContents { get; set; }
        public bool? MoveProcessedFiles { get; set; }
        public string? ProcessedFolder { get; set; }
        public string[]? AllowedExtensions { get; set; }
        public string[]? ExcludePatterns { get; set; }
        public bool? IncludeSubdirectories { get; set; }

        /// <summary>
        /// Timing and retry settings (overrides global defaults when set)
        /// </summary>
        public int? DebounceMilliseconds { get; set; }
        public int? Retries { get; set; }
        public int? RetryDelayMilliseconds { get; set; }
        public int? WaitForFileReadyMilliseconds { get; set; }

        /// <summary>
        /// Maximum allowed execution time for this action in milliseconds. If set and the process
        /// exceeds this duration it will be terminated and treated as a timeout.
        /// Default: 60000 (60 seconds) — persisted when configuration is saved.
        /// </summary>
        public int? ExecutionTimeoutMilliseconds { get; set; }

        /// <summary>
        /// When true, action runners will drain stdout/stderr but not persist or log their contents.
        /// Useful when executing verbose binaries where output should be ignored to avoid large logs.
        /// </summary>
        public bool? IgnoreOutput { get; set; }

        /// <summary>
        /// Content size settings (overrides global defaults when set)
        /// </summary>
        public long? MaxContentBytes { get; set; }
        public long? StreamingThresholdBytes { get; set; }
        public bool? DiscardZeroByteFiles { get; set; }

        /// <summary>
        /// Circuit breaker settings (overrides global defaults when set)
        /// </summary>
        public bool? EnableCircuitBreaker { get; set; }
        public int? CircuitBreakerFailureThreshold { get; set; }
        public int? CircuitBreakerOpenDurationMilliseconds { get; set; }
    }
    public string? ApiEndpoint { get; set; }
    public List<ActionConfig> Actions { get; set; } = [];

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
    public string? DiagnosticsBearerToken { get; set; }
    public int ChannelCapacity { get; set; } = 1000;
    public int MaxParallelSends { get; set; } = 4;
    public int FileWatcherInternalBufferSize { get; set; } = 64 * 1024;
    public int WaitForFileReadyMilliseconds { get; set; }

    /// <summary>
    /// Maximum number of bytes to include when reading file contents for posting.
    /// Files larger than this will be sent without their content to avoid large memory allocations.
    /// Set to 0 to disable content posting regardless of PostFileContents.
    /// </summary>
    public long MaxContentBytes { get; set; } = 5 * 1024 * 1024;
    /// <summary>
    /// 5 MB default
    /// </summary>

    /// <summary>
    /// Threshold in bytes above which the service will stream the file content instead of including it inline in JSON.
    /// Set to 0 to disable streaming and always send as JSON when PostFileContents is enabled.
    /// </summary>
    public long StreamingThresholdBytes { get; set; } = 256 * 1024;
    /// <summary>
    /// 256 KB default
    /// </summary>

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

    /// <summary>
    /// Merge an optional action-level configuration into the provided global configuration.
    /// Action settings take precedence over global defaults. Arrays follow the semantics:
    /// - null action array -> use global
    /// - empty action array   -> use empty (explicitly disable filtering)
    /// </summary>
    /// <param name="global"></param>
    /// <param name="action"></param>
    /// <returns>Merged configuration for runtime use.</returns>
    public static ExternalConfiguration MergeWithAction(ExternalConfiguration global, ActionConfig? action) {
        if (global is null) return new ExternalConfiguration();

        if (action is null) {
            // Return a shallow copy of global to avoid accidental mutation by callers
            return new ExternalConfiguration {
                ApiEndpoint = global.ApiEndpoint,
                BearerToken = global.BearerToken,
                PostFileContents = global.PostFileContents,
                MoveProcessedFiles = global.MoveProcessedFiles,
                ProcessedFolder = global.ProcessedFolder,
                AllowedExtensions = global.AllowedExtensions ?? [],
                ExcludePatterns = global.ExcludePatterns ?? [],
                IncludeSubdirectories = global.IncludeSubdirectories,
                DebounceMilliseconds = global.DebounceMilliseconds,
                Retries = global.Retries,
                RetryDelayMilliseconds = global.RetryDelayMilliseconds,
                WaitForFileReadyMilliseconds = global.WaitForFileReadyMilliseconds,
                MaxContentBytes = global.MaxContentBytes,
                StreamingThresholdBytes = global.StreamingThresholdBytes,
                DiscardZeroByteFiles = global.DiscardZeroByteFiles,
                EnableCircuitBreaker = global.EnableCircuitBreaker,
                CircuitBreakerFailureThreshold = global.CircuitBreakerFailureThreshold,
                CircuitBreakerOpenDurationMilliseconds = global.CircuitBreakerOpenDurationMilliseconds,
                Folders = global.Folders,
                Actions = global.Actions ?? [],
                WatcherMaxRestartAttempts = global.WatcherMaxRestartAttempts,
                WatcherRestartDelayMilliseconds = global.WatcherRestartDelayMilliseconds,
                ChannelCapacity = global.ChannelCapacity,
                MaxParallelSends = global.MaxParallelSends,
                FileWatcherInternalBufferSize = global.FileWatcherInternalBufferSize,
                DiagnosticsUrlPrefix = global.DiagnosticsUrlPrefix,
                DiagnosticsBearerToken = global.DiagnosticsBearerToken,
                Logging = global.Logging
            };
        }

        return new ExternalConfiguration {
            ApiEndpoint = action.ApiEndpoint ?? global.ApiEndpoint,
            BearerToken = action.BearerToken ?? global.BearerToken,

            PostFileContents = action.PostFileContents ?? global.PostFileContents,
            MoveProcessedFiles = action.MoveProcessedFiles ?? global.MoveProcessedFiles,
            ProcessedFolder = action.ProcessedFolder ?? global.ProcessedFolder,
            AllowedExtensions = (action.AllowedExtensions is not null) ? action.AllowedExtensions : (global.AllowedExtensions ?? []),
            ExcludePatterns = (action.ExcludePatterns is not null) ? action.ExcludePatterns : (global.ExcludePatterns ?? []),
            IncludeSubdirectories = action.IncludeSubdirectories ?? global.IncludeSubdirectories,

            DebounceMilliseconds = action.DebounceMilliseconds ?? global.DebounceMilliseconds,
            Retries = action.Retries ?? global.Retries,
            RetryDelayMilliseconds = action.RetryDelayMilliseconds ?? global.RetryDelayMilliseconds,
            WaitForFileReadyMilliseconds = action.WaitForFileReadyMilliseconds ?? global.WaitForFileReadyMilliseconds,

            MaxContentBytes = action.MaxContentBytes ?? global.MaxContentBytes,
            StreamingThresholdBytes = action.StreamingThresholdBytes ?? global.StreamingThresholdBytes,
            DiscardZeroByteFiles = action.DiscardZeroByteFiles ?? global.DiscardZeroByteFiles,

            EnableCircuitBreaker = action.EnableCircuitBreaker ?? global.EnableCircuitBreaker,
            CircuitBreakerFailureThreshold = action.CircuitBreakerFailureThreshold ?? global.CircuitBreakerFailureThreshold,
            CircuitBreakerOpenDurationMilliseconds = action.CircuitBreakerOpenDurationMilliseconds ?? global.CircuitBreakerOpenDurationMilliseconds,

            // Global-only settings
            Folders = global.Folders,
            Actions = global.Actions ?? [],
            WatcherMaxRestartAttempts = global.WatcherMaxRestartAttempts,
            WatcherRestartDelayMilliseconds = global.WatcherRestartDelayMilliseconds,
            ChannelCapacity = global.ChannelCapacity,
            MaxParallelSends = global.MaxParallelSends,
            FileWatcherInternalBufferSize = global.FileWatcherInternalBufferSize,
            DiagnosticsUrlPrefix = global.DiagnosticsUrlPrefix,
            DiagnosticsBearerToken = global.DiagnosticsBearerToken,
            Logging = global.Logging
        };
    }
}

[JsonConverter(typeof(JsonStringEnumConverter<LogType>))]
/// <summary>
/// Use the generic JsonStringEnumConverter<T> to be compatible with Native AOT
/// and avoid SYSLIB1034 warnings about the non-generic converter.
/// </summary>
public enum LogType {
    Csv,
    Json,
    Both
}
