namespace FileWatchRest.Logging;

/// <summary>
/// Unified logger delegates for all core services/components.
/// </summary>
internal static class LoggerDelegates {
    /// <summary>
    /// ExternalConfigurationOptionsMonitor logger delegates (600–699)
    /// </summary>
    internal static readonly Action<ILogger<ExternalConfigurationOptionsMonitor>, string, Exception?> LoadedExcludePatterns =
        LoggerMessage.Define<string>(LogLevel.Information, new EventId(601, "LoadedExcludePatterns"), "Loaded exclude patterns: {Patterns}");
    internal static readonly Action<ILogger<ExternalConfigurationOptionsMonitor>, string, Exception?> ConfigFileNotFound =
        LoggerMessage.Define<string>(LogLevel.Information, new EventId(602, "ConfigFileNotFound"), "Configuration file not found at {Path}, creating default configuration");
    internal static readonly Action<ILogger<ExternalConfigurationOptionsMonitor>, string, Exception?> MigratedLoggingSettings =
        LoggerMessage.Define<string>(LogLevel.Information, new EventId(603, "MigratedLoggingSettings"), "Migrated legacy logging settings to Logging.LogLevel for configuration at {Path}");
    internal static readonly Action<ILogger<ExternalConfigurationOptionsMonitor>, string, Exception?> LoggingMigrationFailed =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(604, "LoggingMigrationFailed"), "Logging migration attempt failed for {Path} - proceeding with deserialized config");
    internal static readonly Action<ILogger<ExternalConfigurationOptionsMonitor>, string, Exception?> ConfigDeserializationFailed =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(605, "ConfigDeserializationFailed"), "Configuration file {Path} could not be deserialized; using defaults");
    internal static readonly Action<ILogger<ExternalConfigurationOptionsMonitor>, string, Exception?> LoadedConfigurationInvalid =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(606, "LoadedConfigurationInvalid"), "Loaded configuration is invalid: {Errors}");
    internal static readonly Action<ILogger<ExternalConfigurationOptionsMonitor>, Exception?> ConfigValidationWarning =
        LoggerMessage.Define(LogLevel.Warning, new EventId(607, "ConfigValidationWarning"), "Configuration validation threw an exception; proceeding with loaded configuration");
    internal static readonly Action<ILogger<ExternalConfigurationOptionsMonitor>, bool, Exception?> CheckingTokenEncryption =
        LoggerMessage.Define<bool>(LogLevel.Debug, new EventId(608, "CheckingTokenEncryption"), "Checking bearer token encryption status. Token starts with 'enc:': {IsEncrypted}");
    internal static readonly Action<ILogger<ExternalConfigurationOptionsMonitor>, Exception?> DecryptingToken =
        LoggerMessage.Define(LogLevel.Information, new EventId(609, "DecryptingToken"), "Encrypted token detected, decrypting for runtime use");
    internal static readonly Action<ILogger<ExternalConfigurationOptionsMonitor>, Exception?> EncryptingToken =
        LoggerMessage.Define(LogLevel.Information, new EventId(610, "EncryptingToken"), "Found plain text bearer token, encrypting for secure storage");
    internal static readonly Action<ILogger<ExternalConfigurationOptionsMonitor>, string, Exception?> FailedToLoad =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(611, "FailedToLoad"), "Failed to load configuration from {Path}, using default");
    internal static readonly Action<ILogger<ExternalConfigurationOptionsMonitor>, string, Exception?> ConfigSaved =
        LoggerMessage.Define<string>(LogLevel.Information, new EventId(612, "ConfigSaved"), "Configuration saved to {Path}");
    internal static readonly Action<ILogger<ExternalConfigurationOptionsMonitor>, string, Exception?> FailedToSave =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(613, "FailedToSave"), "Failed to save configuration to {Path}");
    internal static readonly Action<ILogger<ExternalConfigurationOptionsMonitor>, Exception?> RuntimeValidationWarning =
        LoggerMessage.Define(LogLevel.Warning, new EventId(614, "RuntimeConfigValidationWarning"), "Runtime configuration validation threw an exception; proceeding with decrypted configuration");
    internal static readonly Action<ILogger<ExternalConfigurationOptionsMonitor>, Exception?> EncryptionNotAvailable =
        LoggerMessage.Define(LogLevel.Warning, new EventId(615, "EncryptionNotAvailable"), "Windows encryption not available - bearer token will remain in plain text");
    internal static readonly Action<ILogger<ExternalConfigurationOptionsMonitor>, Exception?> FailedToDecryptToken =
        LoggerMessage.Define(LogLevel.Warning, new EventId(616, "FailedToDecryptToken"), "Failed to decrypt bearer token, treating as plain text");
    internal static readonly Action<ILogger<ExternalConfigurationOptionsMonitor>, string, Exception?> StartedWatchingConfig =
        LoggerMessage.Define<string>(LogLevel.Information, new EventId(617, "StartedWatchingConfig"), "Started watching configuration file {Path}");
    internal static readonly Action<ILogger<ExternalConfigurationOptionsMonitor>, Exception?> FailedToStartWatcher =
        LoggerMessage.Define(LogLevel.Warning, new EventId(618, "FailedToStartWatcher"), "Failed to start watching configuration file");
    internal static readonly Action<ILogger<ExternalConfigurationOptionsMonitor>, string, Exception?> ConfigReloadedInfo =
        LoggerMessage.Define<string>(LogLevel.Information, new EventId(619, "ConfigReloaded"), "Configuration reloaded from {Path}");
    internal static readonly Action<ILogger<ExternalConfigurationOptionsMonitor>, Exception?> FailedReloadWarning =
        LoggerMessage.Define(LogLevel.Warning, new EventId(620, "FailedReloadAfterChange"), "Failed to reload configuration after file change");
    internal static readonly Action<ILogger<ExternalConfigurationOptionsMonitor>, string, Exception?> AutogenDiagnosticsToken =
        LoggerMessage.Define<string>(LogLevel.Information, new EventId(621, "AutogeneratedDiagnosticsToken"), "Generated diagnostics token: {Token}. The token will be persisted to the configuration file and encrypted on Windows.");
    /// <summary>
    /// HttpResilienceService delegates (100–199)
    /// </summary>
    internal static readonly Action<ILogger<HttpResilienceService>, string, Exception?> CircuitOpenWarning =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(101, "CircuitOpen"), "Circuit breaker is open for endpoint {Endpoint}");
    internal static readonly Action<ILogger<HttpResilienceService>, string, int, int, Exception?> PostingTrace =
        LoggerMessage.Define<string, int, int>(LogLevel.Trace, new EventId(102, "PostingAttempt"), "Posting request to {Endpoint} (attempt {Attempt}/{Attempts})");
    internal static readonly Action<ILogger<HttpResilienceService>, int, string, int, int, Exception?> TransientApiWarning =
        LoggerMessage.Define<int, string, int, int>(LogLevel.Warning, new EventId(103, "TransientApiResponse"), "Transient API response {StatusCode} for endpoint {Endpoint} (attempt {Attempt}/{Attempts}) - will retry");
    internal static readonly Action<ILogger<HttpResilienceService>, string, int, Exception?> CircuitOpenedError =
        LoggerMessage.Define<string, int>(LogLevel.Error, new EventId(104, "CircuitOpened"), "Circuit breaker opened for endpoint {Endpoint} due to {Failures} consecutive failures");
    internal static readonly Action<ILogger<HttpResilienceService>, Exception?> AllAttemptsFailedWarning =
        LoggerMessage.Define(LogLevel.Warning, new EventId(105, "AllAttemptsFailed"), "All attempts failed posting to endpoint");
    internal static readonly Action<ILogger<HttpResilienceService>, int, string, Exception?> CircuitOpenedByExceptionError =
        LoggerMessage.Define<int, string>(LogLevel.Error, new EventId(106, "CircuitOpenedByException"), "Circuit breaker opened due to {Failures} consecutive exceptions for endpoint {Endpoint}");
    internal static readonly Action<ILogger<HttpResilienceService>, string, int, int, Exception?> ExceptionPostingWillRetry =
        LoggerMessage.Define<string, int, int>(LogLevel.Debug, new EventId(107, "ExceptionPostingWillRetry"), "Exception posting request to {Endpoint} on attempt {Attempt}/{Attempts}; will retry");
    /// <summary>
    /// ExternalConfigurationOptionsMonitor delegates (200–299)
    /// </summary>
    internal static readonly Action<ILogger<ExternalConfigurationOptionsMonitor>, Exception?> FailedToLoadInitial =
        LoggerMessage.Define(LogLevel.Warning, new EventId(201, "FailedToLoadInitial"), "Failed to load external configuration during options monitor initialization; using defaults");
    internal static readonly Action<ILogger<ExternalConfigurationOptionsMonitor>, Exception?> ErrorHandlingConfigChange =
        LoggerMessage.Define(LogLevel.Warning, new EventId(202, "ErrorHandlingConfigChange"), "Error handling external configuration change");
    internal static readonly Action<ILogger<ExternalConfigurationOptionsMonitor>, Exception?> ListenerThrew =
        LoggerMessage.Define(LogLevel.Warning, new EventId(204, "ListenerThrew"), "Listener threw while handling configuration change");
    internal static readonly Action<ILogger<ExternalConfigurationOptionsMonitor>, Exception?> FailedToStartWatcherConfig =
        LoggerMessage.Define(LogLevel.Warning, new EventId(203, "FailedToStartWatcherConfig"), "Failed to start watching external configuration file.");

    /// <summary>
    /// FileWatcherManager delegates (300–399)
    /// </summary>
    internal static readonly Action<ILogger<FileWatcherManager>, string, Exception?> WatchingFolder =
        LoggerMessage.Define<string>(LogLevel.Information, new EventId(301, "WatchingFolder"), "Watching folder: {Folder}");
    internal static readonly Action<ILogger<FileWatcherManager>, string, Exception?> FailedToWatchFolder =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(302, "FailedToWatchFolder"), "Failed to watch folder {Folder}");
    internal static readonly Action<ILogger<FileWatcherManager>, string, Exception?> WatcherError =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(303, "WatcherError"), "FileSystemWatcher error for folder {Folder}");
    internal static readonly Action<ILogger<FileWatcherManager>, string, int, int, Exception?> AttemptingRestart =
        LoggerMessage.Define<string, int, int>(LogLevel.Information, new EventId(304, "AttemptingRestart"), "Attempting to restart watcher for {Folder} (attempt {Attempt}/{Max})");
    internal static readonly Action<ILogger<FileWatcherManager>, string, int, Exception?> WatcherFailedAfterMax =
        LoggerMessage.Define<string, int>(LogLevel.Error, new EventId(305, "WatcherFailedAfterMax"), "Watcher for {Folder} failed after {Max} attempts");
    internal static readonly Action<ILogger<FileWatcherManager>, string, Exception?> WatcherRestarted =
        LoggerMessage.Define<string>(LogLevel.Information, new EventId(306, "WatcherRestarted"), "Successfully restarted watcher for {Folder}");
    internal static readonly Action<ILogger<FileWatcherManager>, string, Exception?> FailedHandlingWatcherError =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(307, "FailedHandlingWatcherError"), "Failed handling watcher error for {Folder}");
    internal static readonly Action<ILogger<FileWatcherManager>, string, Exception?> FailedRestartWatcher =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(308, "FailedRestartWatcher"), "Failed to restart watcher for {Folder}");
    internal static readonly Action<ILogger<FileWatcherManager>, string, Exception?> ErrorStoppingWatcher =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(309, "ErrorStoppingWatcher"), "Error stopping watcher for {Path}");

    /// <summary>
    /// Worker logger delegates (400–499)
    /// </summary>
    internal static readonly Action<ILogger, string, Exception?> ConfigFilePathLoaded =
        LoggerMessage.Define<string>(LogLevel.Information, new EventId(401, nameof(ConfigFilePathLoaded)), "Loaded config file path {Path}");
    internal static readonly Action<ILogger, int, Exception?> LoadedConfig =
        LoggerMessage.Define<int>(LogLevel.Information, new EventId(402, nameof(LoadedConfig)), "Loaded config with {Count} folders");
    internal static readonly Action<ILogger, Exception?> NoFoldersConfigured =
        LoggerMessage.Define(LogLevel.Warning, new EventId(403, nameof(NoFoldersConfigured)), "No folders configured");
    internal static readonly Action<ILogger, string, Exception?> WatcherFailedStopping =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(404, nameof(WatcherFailedStopping)), "Failed stopping watcher for {Folder}");
    internal static readonly Action<ILogger, string, Exception?> IgnoredFileInProcessed =
        LoggerMessage.Define<string>(LogLevel.Information, new EventId(405, nameof(IgnoredFileInProcessed)), "Ignored file in processed folder: {File}");
    internal static readonly Action<ILogger, string, Exception?> QueuedDebouncedTrace =
        LoggerMessage.Define<string>(LogLevel.Trace, new EventId(406, nameof(QueuedDebouncedTrace)), "Queued debounced event for {File}");
    internal static readonly Action<ILogger, string, Exception?> ErrorProcessingFileWarning =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(407, nameof(ErrorProcessingFileWarning)), "Error processing file {File}");
    internal static readonly Action<ILogger, string, Exception?> FailedSchedulingDebounce =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(408, nameof(FailedSchedulingDebounce)), "Failed scheduling debounce for {File}");
    internal static readonly Action<ILogger, string, long?, bool, Exception?> CreatedNotificationDebug =
        LoggerMessage.Define<string, long?, bool>(LogLevel.Debug, new EventId(409, nameof(CreatedNotificationDebug)), "Created notification for {File}, size: {Size}, isNew: {IsNew}");
    internal static readonly Action<ILogger, string, Exception?> ApiEndpointNotConfigured =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(410, nameof(ApiEndpointNotConfigured)), "API endpoint not configured for {Folder}");
    internal static readonly Action<ILogger, string, bool, bool, Exception?> ProcessingFileTrace =
        LoggerMessage.Define<string, bool, bool>(LogLevel.Trace, new EventId(411, nameof(ProcessingFileTrace)), "Processing file {File}, isNew: {IsNew}, isValid: {IsValid}");
    internal static readonly Action<ILogger, Exception?> FailedToProcessFile =
        LoggerMessage.Define(LogLevel.Error, new EventId(412, nameof(FailedToProcessFile)), "Failed to process file");
    internal static readonly Action<ILogger, string, long, Exception?> FileExceedsMaxContentWarning =
        LoggerMessage.Define<string, long>(LogLevel.Warning, new EventId(413, nameof(FileExceedsMaxContentWarning)), "File {File} exceeds max content size: {Size}");
    internal static readonly Action<ILogger, string, Exception?> FailedReadFileWarning =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(414, nameof(FailedReadFileWarning)), "Failed to read file {File}");
    internal static readonly Action<ILogger, string, string, Exception?> AttachingAuthDebug =
        LoggerMessage.Define<string, string>(LogLevel.Debug, new EventId(415, nameof(AttachingAuthDebug)), "Attaching auth for {Endpoint} with key {Key}");
    internal static readonly Action<ILogger, string, string, Exception?> CircuitOpenSkipWarning =
        LoggerMessage.Define<string, string>(LogLevel.Warning, new EventId(416, nameof(CircuitOpenSkipWarning)), "Circuit open, skipping {Action} for {Endpoint}");
    internal static readonly Action<ILogger, string, int, long, int, Exception?> SuccessPostedInfo =
        LoggerMessage.Define<string, int, long, int>(LogLevel.Information, new EventId(417, nameof(SuccessPostedInfo)), "Successfully posted {Action}, status: {Status}, duration: {Duration}ms, retries: {Retries}");
    internal static readonly Action<ILogger, string, int, Exception?> FailedPostError =
        LoggerMessage.Define<string, int>(LogLevel.Error, new EventId(418, nameof(FailedPostError)), "Failed to post {Action}, status: {Status}");
    internal static readonly Action<ILogger, string, string, Exception?> MovedProcessedInfo =
        LoggerMessage.Define<string, string>(LogLevel.Information, new EventId(419, nameof(MovedProcessedInfo)), "Moved processed file {File} to {Destination}");
    internal static readonly Action<ILogger, string, string, Exception?> ExcludedByPattern =
        LoggerMessage.Define<string, string>(LogLevel.Information, new EventId(420, nameof(ExcludedByPattern)), "Excluded {File} by pattern {Pattern}");
    internal static readonly Action<ILogger, string, Exception?> LogFailedQueueExistingFileWarning =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(421, nameof(LogFailedQueueExistingFileWarning)), "Failed to queue existing file {File}");
    /// <summary>
    /// DiagnosticsService logger delegates (500–599)
    /// </summary>
    internal static readonly Action<ILogger<DiagnosticsService>, string, Exception?> AttemptStartServer =
        LoggerMessage.Define<string>(LogLevel.Information, new EventId(501, "AttemptStartServer"), "Attempting to start diagnostics HTTP server at {Url}");
    internal static readonly Action<ILogger<DiagnosticsService>, string, Exception?> ServerStarted =
        LoggerMessage.Define<string>(LogLevel.Information, new EventId(502, "ServerStarted"), "Diagnostics HTTP server started successfully at {Url}");
    internal static readonly Action<ILogger<DiagnosticsService>, string, int, Exception?> FailedStartServer =
        LoggerMessage.Define<string, int>(LogLevel.Error, new EventId(503, "FailedStartServer"), "Failed to start diagnostics HTTP server at {Url}. Error code: {ErrorCode}.");
    internal static readonly Action<ILogger<DiagnosticsService>, Exception?> HttpServerError =
        LoggerMessage.Define(LogLevel.Warning, new EventId(504, "HttpServerError"), "Error in diagnostics HTTP server");
    internal static readonly Action<ILogger<DiagnosticsService>, Exception?> SerializeStatusError =
        LoggerMessage.Define(LogLevel.Error, new EventId(505, "SerializeStatusError"), "Failed to serialize status response");
    internal static readonly Action<ILogger<DiagnosticsService>, Exception?> SerializeHealthError =
        LoggerMessage.Define(LogLevel.Error, new EventId(506, "SerializeHealthError"), "Failed to serialize health response");
    internal static readonly Action<ILogger<DiagnosticsService>, string, Exception?> ProcessRequestError =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(507, "ProcessRequestError"), "Error processing diagnostics request for path: {Path}");
    internal static readonly Action<ILogger<DiagnosticsService>, Exception?> DisposeError =
        LoggerMessage.Define(LogLevel.Warning, new EventId(508, "DisposeError"), "Error disposing diagnostics service");
    internal static readonly Action<ILogger<DiagnosticsService>, string, Exception?> FailedStartServerGeneral =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(509, "FailedStartServerGeneral"), "Failed to start diagnostics HTTP server at {Url}");
    internal static readonly Action<ILogger, string, string, Exception?> CheckingPattern =
        LoggerMessage.Define<string, string>(LogLevel.Debug, new EventId(510, nameof(CheckingPattern)), "Checking pattern {Pattern} for folder {Folder}");
    /// <summary>
    /// Action delegates (700–799)
    /// </summary>
    internal static readonly Action<ILogger<PowerShellScriptAction>, string, string, Exception?> PowerShellOutputXml =
        LoggerMessage.Define<string, string>(LogLevel.Information, new EventId(701, "PowerShellOutputXml"), "PowerShell output XML for {ScriptPath}: {Xml}");

    /// <summary>
    /// Generic upload result used by tests to validate provider structured fields
    /// </summary>
    internal static readonly Action<ILogger, string, int, Exception?> UploadResult =
        LoggerMessage.Define<string, int>(LogLevel.Information, new EventId(702, "UploadResult"), "Upload result for {Path} with StatusCode {StatusCode}");
    /// <summary>
    /// FileSenderService delegates (800–809)
    /// </summary>
    internal static readonly Action<ILogger<FileSenderService>, Exception?> FileSenderStarted =
        LoggerMessage.Define(LogLevel.Information, new EventId(800, "FileSenderStarted"), "FileSenderService started");
    internal static readonly Action<ILogger<FileSenderService>, Exception?> FileSenderStopped =
        LoggerMessage.Define(LogLevel.Information, new EventId(801, "FileSenderStopped"), "FileSenderService stopped");
    internal static readonly Action<ILogger<FileSenderService>, string, Exception?> FileSenderError =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(802, "FileSenderError"), "Error processing file: {Path}");

    /// <summary>
    /// FileDebounceService delegates (810–819)
    /// </summary>
    internal static readonly Action<ILogger<FileDebounceService>, Exception?> FileDebounceStarted =
        LoggerMessage.Define(LogLevel.Information, new EventId(810, "FileDebounceStarted"), "FileDebounceService started");
    internal static readonly Action<ILogger<FileDebounceService>, string, Exception?> FileDebounceWriteTimeout =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(811, "FileDebounceWriteTimeout"), "Failed to write {Path} to channel (timeout)");
    internal static readonly Action<ILogger<FileDebounceService>, Exception?> FileDebounceError =
        LoggerMessage.Define(LogLevel.Error, new EventId(812, "FileDebounceError"), "Error in debounce scheduler loop");
    internal static readonly Action<ILogger<FileDebounceService>, Exception?> FileDebounceStopped =
        LoggerMessage.Define(LogLevel.Information, new EventId(813, "FileDebounceStopped"), "FileDebounceService stopped");

    /// <summary>
    /// Worker delegates (830–839)
    /// </summary>
    internal static readonly Action<ILogger<Worker>, string, string, Exception?> FileRenamed =
        LoggerMessage.Define<string, string>(LogLevel.Debug, new EventId(830, "FileRenamed"), "File renamed: {OldPath} -> {NewPath}");
    internal static readonly Action<ILogger<Worker>, string, int, Exception?> CheckingExcludePatterns =
        LoggerMessage.Define<string, int>(LogLevel.Trace, new EventId(831, "CheckingExcludePatterns"), "Checking file '{FileName}' against {Count} exclude patterns");
    internal static readonly Action<ILogger<Worker>, string, string, Exception?> NotExcludedPatterns =
        LoggerMessage.Define<string, string>(LogLevel.Debug, new EventId(832, "NotExcludedPatterns"), "File '{FileName}' was NOT excluded by any pattern. Patterns checked: {Patterns}");
    internal static readonly Action<ILogger<Worker>, string, Exception?> SkippingAlreadyPosted =
        LoggerMessage.Define<string>(LogLevel.Debug, new EventId(833, "SkippingAlreadyPosted"), "Skipping already posted file: {FilePath}");
    /// <summary>
    /// Worker error and progress delegates (400–499)
    /// </summary>
    public static readonly Action<ILogger, string, Exception?> FailedToMoveProcessedFile =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(422, nameof(FailedToMoveProcessedFile)), "Failed to move processed file: {FilePath}");
    public static readonly Action<ILogger, Exception?> FailedConfigReload =
        LoggerMessage.Define(LogLevel.Error, new EventId(423, nameof(FailedConfigReload)), "Failed to reload configuration.");
    public static readonly Action<ILogger, string, Exception?> FolderNotFoundWarning =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(424, nameof(FolderNotFoundWarning)), "Configured folder not found: {FolderPath}");
    public static readonly Action<ILogger, string, int, Exception?> EnqueueProgressInfo =
        LoggerMessage.Define<string, int>(LogLevel.Information, new EventId(425, nameof(EnqueueProgressInfo)), "Enqueued {Count} files from folder: {FolderPath}");
    public static readonly Action<ILogger, string, int, Exception?> FolderScanCompleteInfo =
        LoggerMessage.Define<string, int>(LogLevel.Information, new EventId(426, nameof(FolderScanCompleteInfo)), "Completed scan of folder: {FolderPath}, total files enqueued: {Count}");
    public static readonly Action<ILogger, string, Exception?> FailedEnqueueExistingFiles =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(427, nameof(FailedEnqueueExistingFiles)), "Failed to enqueue existing files for folder: {FolderPath}");
    public static readonly Action<ILogger, string, Exception?> FailedToConfigureLogging =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(428, nameof(FailedToConfigureLogging)), "Failed to configure logging: {ErrorMessage}");

}
