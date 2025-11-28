
namespace FileWatchRest.Services;

public partial class Worker : BackgroundService {
    private static readonly ActivitySource ActivitySource = new("FileWatchRest.Worker", "1.0.0");

    private readonly ILogger<Worker> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    private readonly FileWatcherManager _fileWatcherManager;
    private readonly FileDebounceService _debounceService;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly DiagnosticsService _diagnostics;
    private readonly SemaphoreSlim _configReloadLock = new(1, 1);
    private readonly IResilienceService _resilienceService;
    private readonly IOptionsMonitor<ExternalConfiguration> _optionsMonitor;
    private IDisposable? _optionsSubscription;

    /// <summary>
    /// Expose current configuration for tests and controlled updates
    /// </summary>
    internal ExternalConfiguration CurrentConfig { get; set; }
    public Worker(
        ILogger<Worker> logger,
        IHttpClientFactory httpClientFactory,
        IHostApplicationLifetime lifetime,
        DiagnosticsService diagnostics,
        FileWatcherManager fileWatcherManager,
        FileDebounceService debounceService,
        IResilienceService resilienceService,
        IOptionsMonitor<ExternalConfiguration> optionsMonitor) {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _lifetime = lifetime;
        _diagnostics = diagnostics;

        _fileWatcherManager = fileWatcherManager;
        _debounceService = debounceService;
        _resilienceService = resilienceService;
        _optionsMonitor = optionsMonitor;
        // Initialize _currentConfig from options monitor
        CurrentConfig = _optionsMonitor.CurrentValue ?? new ExternalConfiguration();
        // Subscribe to configuration changes
        // Sync handler for tests: propagate changes via OnConfigurationChanged only
        _optionsMonitor.OnChange(config => {
            CurrentConfig = config ?? new ExternalConfiguration();
            OnConfigurationChanged(CurrentConfig).GetAwaiter().GetResult();
        });
    }



    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        // Configuration is provided via IOptionsMonitor; current value already initialized.
        ConfigureLogging(CurrentConfig.Logging?.LogLevel.ToString() ?? "Information");
        LoggerDelegates.LoadedConfig(_logger, CurrentConfig.Folders.Count, null);

        // Configure folder actions from Folders list
        try {
            _fileWatcherManager.ConfigureFolderActions(CurrentConfig.Folders, CurrentConfig, this);
        }
        catch (Exception ex) {
            LoggerDelegates.FailedConfigReload(_logger, ex);
        }

        // Configure diagnostics access token and start HTTP server
        _diagnostics.SetBearerToken(CurrentConfig.DiagnosticsBearerToken);
        _diagnostics.SetConfiguration(CurrentConfig);
        _diagnostics.StartHttpServer(CurrentConfig.DiagnosticsUrlPrefix);

        // Start watching for configuration changes via options monitor (monitor registers the file watcher)
        _optionsSubscription = _optionsMonitor.OnChange(async (newCfg, name) => await OnConfigurationChanged(newCfg));

        if (CurrentConfig.Folders.Count == 0) {
            LoggerDelegates.NoFoldersConfigured(_logger, null);
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            return;
        }

        await StartWatchingFoldersAsync(CurrentConfig.Folders);

        // Background services (FileDebounceService, FileSenderService) handle debouncing and sending
        // Worker now only coordinates file watching and event routing

        // Enqueue existing files that were added while service was down
        await EnqueueExistingFilesAsync(stoppingToken);

        try {
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
            // Exit gracefully on shutdown
        }

        // Cleanup
        await CleanupAsync();
    }

    private Task StartWatchingFoldersAsync(List<ExternalConfiguration.WatchedFolderConfig> folders) {
        return _fileWatcherManager.StartWatchingAsync(folders, CurrentConfig, OnFileChanged, OnWatcherError, folderPath => {
            // Exceeded restart attempts -> stop application
            LoggerDelegates.WatcherFailedStopping(_logger, folderPath, null);
            _lifetime.StopApplication();
        });
    }

    private void OnFileChanged(object? sender, FileSystemEventArgs e) {
        // Accept Created, Changed, and Renamed events
        // Renamed events occur when files are moved into the watched folder
        if (e.ChangeType is not (WatcherChangeTypes.Created or WatcherChangeTypes.Changed or WatcherChangeTypes.Renamed)) {
            return;
        }

        string path = e.FullPath;
        string? oldPath = null;
        if (e is RenamedEventArgs renamed) {
            oldPath = renamed.OldFullPath;
            LoggerDelegates.FileRenamed(_logger, oldPath ?? string.Empty, path, null);
        }

        // Determine effective config for this path (merge action with global defaults)
        ExternalConfiguration cfg = GetConfigForPath(path);

        // Exclude files in the processed folder to prevent infinite loops
        string[] pathParts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (pathParts.Any(part => part.Equals(cfg.ProcessedFolder, StringComparison.OrdinalIgnoreCase))) {
            LoggerDelegates.IgnoredFileInProcessed(_logger, path, null);
            return;
        }

        // Apply file extension filtering
        if (cfg.AllowedExtensions is { Length: > 0 }) {
            string extension = Path.GetExtension(path);
            if (!cfg.AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase)) {
                return;
            }
        }

        // Apply exclude pattern filtering
        if (cfg.ExcludePatterns is { Length: > 0 }) {
            string fileName = Path.GetFileName(path);

            LoggerDelegates.CheckingExcludePatterns(_logger, fileName, cfg.ExcludePatterns.Length, null);

            // Use framework-based pattern matcher (System.IO.Enumeration.FileSystemName)
            string? matchedPattern = FileSystemPatternMatcher.TryMatchAny(fileName, cfg.ExcludePatterns);
            if (matchedPattern is not null) {
                LoggerDelegates.ExcludedByPattern(_logger, fileName, matchedPattern, null);
                return;
            }

            if (_logger.IsEnabled(LogLevel.Debug)) {
                LoggerDelegates.NotExcludedPatterns(_logger, fileName, string.Join(", ", cfg.ExcludePatterns), null);
            }
        }

        // Exclude files that have already been posted
        if (_diagnostics.IsFilePosted(path)) {
            LoggerDelegates.SkippingAlreadyPosted(_logger, path, null);
            return;
        }

        // Schedule debounced processing via FileDebounceService
        _debounceService.Schedule(path);

        LoggerDelegates.QueuedDebouncedTrace(_logger, path, null);
    }

    /// <summary>
    /// Enqueue a path coming from a folder action in a debounced fashion.
    /// This allows folder actions (e.g. RestPostAction) to schedule work without
    /// invoking the processing pipeline directly, which prevents duplicate
    /// immediate posts when multiple watcher events fire for the same file.
    /// </summary>
    /// <param name="path"></param>
    internal void EnqueueFileFromAction(string path) {
        if (_diagnostics.IsFilePosted(path)) {
            LoggerDelegates.SkippingAlreadyPosted(_logger, path, null);
            return;
        }

        _debounceService.Schedule(path);
        LoggerDelegates.QueuedDebouncedTrace(_logger, path, null);
    }

    /// <summary>
    /// Process a single file notification. Made internal for direct unit testing.
    /// </summary>
    /// <param name="path"></param>
    /// <param name="ct"></param>
    internal async ValueTask ProcessFileAsync(string path, CancellationToken ct) {
        try {
            // Use diagnostics to check if file was already posted and acknowledged
            if (_diagnostics.IsFilePosted(path)) {
                LoggerDelegates.SkippingAlreadyPosted(_logger, path, null);
                return;
            }
            ExternalConfiguration cfg = GetConfigForPath(path);

            if (cfg.WaitForFileReadyMilliseconds > 0) {
                bool ready = await WaitForFileReadyAsync(path, cfg, ct);
                if (!ready) {
                    // Configured to discard zero-length files and file remained empty - skip processing
                    _diagnostics.RecordFileEvent(path, false, null);
                    return;
                }
            }

            // Only attempt to POST to an API when the configured action for this path is a RestPost.
            ExternalConfiguration.FolderActionType? actionType = _fileWatcherManager.GetActionTypeForPath(path);
            if (actionType != ExternalConfiguration.FolderActionType.RestPost) {
                // Not a REST-targeted action; nothing for Worker to send here.
                return;
            }

            FileNotification notification = await CreateNotificationAsync(path, cfg, ct);
            LoggerDelegates.CreatedNotificationDebug(_logger, path, notification.FileSize, !string.IsNullOrEmpty(notification.Content), null);

            bool success = await SendNotificationAsync(notification, cfg, ct);

            if (success && cfg.MoveProcessedFiles) {
                await MoveToProcessedFolderAsync(path, cfg, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) {
            // Expected during shutdown
        }
        catch (Exception ex) {
            LoggerDelegates.ErrorProcessingFileWarning(_logger, path, ex);
            _diagnostics.RecordFileEvent(path, false, null);
        }
    }

    /// Determine the configured action type for a given file path by matching the most specific watched folder and
    /// <summary>
    /// Determine the configured action type for a given file path by matching the most specific watched folder and
    /// </summary>
    /// <param name="path"></param>
    /// <returns></returns>.
    // Action type lookup is provided by FileWatcherManager via a precomputed mapping.

    internal static async Task<bool> WaitForFileReadyAsync(string path, ExternalConfiguration cfg, CancellationToken ct) {
        var sw = Stopwatch.StartNew();
        bool waited = false;
        int waitMs = Math.Max(0, cfg.WaitForFileReadyMilliseconds);
        // Replace with direct logger call or add to LoggerDelegates if needed

        while (sw.ElapsedMilliseconds < waitMs) {
            try {
                // Try to open for read to ensure the writer has released any exclusive locks
                using FileStream fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);

                if (!cfg.PostFileContents) {
                    // We don't need content - file is considered ready if it can be opened
                    return true;
                }

                // If PostFileContents is configured, wait until file length is non-zero
                if (fs.Length > 0) {
                    return true;
                }

                // File exists and is readable but empty; continue waiting
                waited = true;
            }
            catch {
                // File not ready (locked/not present) - keep waiting
            }

            try { await Task.Delay(50, ct); } catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
        }

        if (waited) {
            // Replace with direct logger call or add to LoggerDelegates if needed
        }

        // If configured to discard zero-length files, indicate caller to skip processing
        if (cfg.DiscardZeroByteFiles) {
            // Replace with direct logger call or add to LoggerDelegates if needed
            return false;
        }

        // Otherwise, proceed (will process zero-length file)
        return true;
    }

    internal ValueTask<FileNotification> CreateNotificationAsync(string path, ExternalConfiguration cfg, CancellationToken ct) {
        var fileInfo = new FileInfo(path);
        var notification = new FileNotification {
            Path = path,
            FileSize = fileInfo.Length,
            LastWriteTime = fileInfo.LastWriteTime
        };

        // Fast path: No content posting - return synchronously to avoid Task allocation
        if (!cfg.PostFileContents) {
            return new ValueTask<FileNotification>(notification);
        }

        // Async path: Need to read file contents
        return new ValueTask<FileNotification>(CreateNotificationWithContentAsync(notification, fileInfo, path, cfg, ct));
    }

    // Back-compat overload: use current global config
    internal ValueTask<FileNotification> CreateNotificationAsync(string path, CancellationToken ct) => CreateNotificationAsync(path, CurrentConfig, ct);

    private async Task<FileNotification> CreateNotificationWithContentAsync(
        FileNotification notification, FileInfo fileInfo, string path, ExternalConfiguration cfg, CancellationToken ct) {
        try {
            // Enforce a maximum content size to avoid large memory allocations
            if (fileInfo.Length <= cfg.MaxContentBytes) {
                // Use ArrayPool for better memory efficiency on larger files
                if (fileInfo.Length > 4096) // Use pooling for files > 4KB
                {
                    int byteCount = (int)fileInfo.Length;
                    byte[] buffer = ArrayPool<byte>.Shared.Rent(byteCount);
                    try {
                        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
                        int totalRead = 0;
                        while (totalRead < byteCount) {
                            int read = await fs.ReadAsync(buffer.AsMemory(totalRead, byteCount - totalRead), ct);
                            if (read == 0) {
                                break;
                            }

                            totalRead += read;
                        }
                        notification.Content = Encoding.UTF8.GetString(buffer, 0, totalRead);
                    }
                    finally {
                        ArrayPool<byte>.Shared.Return(buffer);
                    }
                }
                else {
                    // Small files: use simple read (< 4KB)
                    notification.Content = await File.ReadAllTextAsync(path, ct);
                }
            }
            else {
                LoggerDelegates.FileExceedsMaxContentWarning(_logger, path, cfg.MaxContentBytes, null);
                notification.Content = null; // Do not include contents that exceed the configured limit
            }
        }
        catch (Exception ex) {
            LoggerDelegates.FailedReadFileWarning(_logger, path, ex);
            // Continue without contents
        }

        return notification;
    }

    /// <summary>
    /// Helper extracted from SendNotificationAsync to decide whether to use multipart streaming for a given notification.
    /// </summary>
    /// <param name="notification"></param>
    /// <returns></returns>
    internal static bool ShouldUseStreamingUpload(FileNotification notification, ExternalConfiguration cfg) {
        if (!cfg.PostFileContents) {
            return false;
        }

        if (!notification.FileSize.HasValue) {
            return false;
        }

        long size = notification.FileSize.Value;
        long threshold = Math.Max(0, cfg.StreamingThresholdBytes);
        return size > threshold && size <= cfg.MaxContentBytes;
    }

    // Back-compat overloads for tests and callers that expect no-config variants
    internal bool ShouldUseStreamingUpload(FileNotification notification) => ShouldUseStreamingUpload(notification, CurrentConfig);

    internal Task<bool> SendNotificationAsync(FileNotification notification, CancellationToken ct) => SendNotificationAsync(notification, CurrentConfig, ct);

    internal async Task<bool> SendNotificationAsync(FileNotification notification, ExternalConfiguration cfg, CancellationToken ct) {
        if (string.IsNullOrWhiteSpace(cfg.ApiEndpoint)) {
            LoggerDelegates.ApiEndpointNotConfigured(_logger, notification.Path, null);
            return false;
        }

        using HttpClient fileClient = _httpClientFactory.CreateClient("fileApi");

        // Prepare a factory to create a fresh HttpRequestMessage for each attempt so streaming content is created per attempt.
        Task<HttpRequestMessage> requestFactory(CancellationToken ct) {
            // Decide sending strategy: use streaming upload when appropriate
            if (ShouldUseStreamingUpload(notification, cfg)) {
                var metadataObj = new UploadMetadata {
                    Path = notification.Path,
                    FileSize = notification.FileSize,
                    LastWriteTime = notification.LastWriteTime
                };

                var multipart = new MultipartFormDataContent();
                string metadataJson = JsonSerializer.Serialize(metadataObj, MyJsonContext.Default.UploadMetadata);
                var metadataContent = new StringContent(metadataJson, Encoding.UTF8);
                metadataContent.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                multipart.Add(metadataContent, "metadata");

                // Fix: Ensure file handle is disposed if exception occurs before request is sent
                FileStream? fs = null;
                try {
                    fs = File.Open(notification.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
                    var streamContent = new StreamContent(fs);
                    fs = null; // Transfer ownership to StreamContent
                    streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                    multipart.Add(streamContent, "file", Path.GetFileName(notification.Path));

                    var reqMsg = new HttpRequestMessage(HttpMethod.Post, cfg.ApiEndpoint) { Content = multipart };
                    if (!string.IsNullOrWhiteSpace(cfg.BearerToken)) {
                        string token = cfg.BearerToken;
                        if (token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) {
                            token = token[7..].Trim();
                        }

                        reqMsg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                        LoggerDelegates.AttachingAuthDebug(_logger, cfg.ApiEndpoint ?? string.Empty, token, null);
                    }

                    // Use Activity API for W3C distributed tracing
                    using Activity? activity = ActivitySource.StartActivity("SendNotification", ActivityKind.Client);
                    activity?.SetTag("file.path", notification.Path);
                    activity?.SetTag("file.size", notification.FileSize);
                    activity?.SetTag("http.method", "POST");
                    activity?.SetTag("http.url", cfg.ApiEndpoint);

                    // W3C trace context is automatically propagated by HttpClient when Activity is active
                    return Task.FromResult(reqMsg);
                }
                finally {
                    // Dispose file stream if ownership was not transferred
                    fs?.Dispose();
                }
            }
            else {
                byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(notification, MyJsonContext.Default.FileNotification);
                var ms = new MemoryStream(bytes, writable: false);
                var content = new StreamContent(ms);
                content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                var reqMsg = new HttpRequestMessage(HttpMethod.Post, cfg.ApiEndpoint) { Content = content };
                if (!string.IsNullOrWhiteSpace(cfg.BearerToken)) {
                    string token = cfg.BearerToken;
                    if (token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) {
                        token = token[7..].Trim();
                    }

                    reqMsg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                    LoggerDelegates.AttachingAuthDebug(_logger, cfg.ApiEndpoint ?? string.Empty, token, null);
                }
                // Correlate request with a unique id for tracing
                string requestId = Guid.NewGuid().ToString("N");
                if (!reqMsg.Headers.Contains("X-Request-Id")) {
                    reqMsg.Headers.Add("X-Request-Id", requestId);
                }
                // Replace with direct logger call or add to LoggerDelegates if needed
                return Task.FromResult(reqMsg);
            }
        }

        string endpointKey = string.IsNullOrWhiteSpace(cfg.ApiEndpoint) ? string.Empty : cfg.ApiEndpoint;

        // Use merged configuration directly (no conversion needed)
        ResilienceResult result = await _resilienceService.SendWithRetriesAsync(requestFactory, fileClient, endpointKey, cfg, ct);

        // Diagnostics and logging for this file are recorded here using the worker's notification path
        _diagnostics.RecordFileEvent(notification.Path, result.Success, result.LastStatusCode);

        if (result.ShortCircuited) {
            LoggerDelegates.CircuitOpenSkipWarning(_logger, endpointKey ?? string.Empty, notification.Path, null);
            return false;
        }

        if (result.Success) {
            LoggerDelegates.SuccessPostedInfo(_logger, notification.Path, result.LastStatusCode ?? 0, result.TotalElapsedMs, result.Attempts, null);
            return true;
        }

        // Log specific error details
        if (result.LastStatusCode.HasValue && result.LastStatusCode.Value >= 400) {
            string reasonPhrase = result.LastException is HttpRequestException httpEx && httpEx.StatusCode.HasValue
            ? httpEx.StatusCode.Value.ToString()
            : result.LastStatusCode.Value.ToString(CultureInfo.InvariantCulture);
            // Consider direct logger call or add to LoggerDelegates if needed
        }

        if (result.LastException is not null) {
            // Categorize exception types for better diagnostics
            if (result.LastException is TimeoutException or TaskCanceledException) {
                // Consider direct logger call or add to LoggerDelegates if needed
            }
            else if (result.LastException is HttpRequestException) {
                // Consider direct logger call or add to LoggerDelegates if needed
            }
            else {
                LoggerDelegates.FailedPostError(_logger, notification.Path, result.Attempts, result.LastException);
            }
        }

        return false;
    }

    private Task MoveToProcessedFolderAsync(string filePath, ExternalConfiguration cfg, CancellationToken ct) {
        try {
            string directory = Path.GetDirectoryName(filePath)!;
            string processedDir = Path.Combine(directory, cfg.ProcessedFolder);

            Directory.CreateDirectory(processedDir);

            string fileName = Path.GetFileName(filePath);
            string nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
            string extension = Path.GetExtension(fileName);

            // Add datetime prefix for uniqueness and traceability
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
            string processedFileName = $"{timestamp}_{nameWithoutExt}{extension}";
            string processedPath = Path.Combine(processedDir, processedFileName);

            // Handle extremely rare case where files are processed at exact same millisecond
            int counter = 1;
            while (File.Exists(processedPath)) {
                processedFileName = $"{timestamp}_{nameWithoutExt}_{counter}{extension}";
                processedPath = Path.Combine(processedDir, processedFileName);
                counter++;
            }

            File.Move(filePath, processedPath);
            LoggerDelegates.MovedProcessedInfo(_logger, filePath, processedPath, null);
        }
        catch (Exception ex) {
            LoggerDelegates.FailedToMoveProcessedFile(_logger, filePath, ex);
        }

        return Task.CompletedTask;
    }

    private async Task OnConfigurationChanged(ExternalConfiguration newConfig) {
        if (!await _configReloadLock.WaitAsync(0)) {
            return; // Already reloading
        }

        try {
            // Do NOT set _currentConfig here; sync OnChange handler already did
            // Update diagnostics with the new configuration (allows /config endpoint to return latest settings)
            // _diagnostics.SetConfiguration(newConfig); // No longer needed

            // Rebuild folder action mapping for new configuration
            // Reconfigure folder actions from Folders list
            _fileWatcherManager.ConfigureFolderActions(newConfig.Folders, newConfig, this);

            // Stop current watchers via manager
            await _fileWatcherManager.StopAllAsync();

            // Start new watchers
            if (newConfig.Folders.Count > 0) {
                await StartWatchingFoldersAsync(newConfig.Folders);
            }

            // Apply logging configuration from the reloaded config
            ConfigureLogging(newConfig.Logging?.LogLevel.ToString() ?? "Information");
            // Update diagnostics bearer token and configuration in case it changed
            _diagnostics.SetBearerToken(newConfig.DiagnosticsBearerToken);
            _diagnostics.SetConfiguration(newConfig);
            // Restart diagnostics HTTP server if prefix changed (listener only starts once otherwise)
            if (!string.Equals(newConfig.DiagnosticsUrlPrefix, _diagnostics.CurrentPrefix, StringComparison.OrdinalIgnoreCase)) {
                _diagnostics.RestartHttpServer(newConfig.DiagnosticsUrlPrefix);
            }
        }
        catch (Exception ex) {
            LoggerDelegates.FailedConfigReload(_logger, ex);
        }
        finally {
            _configReloadLock.Release();
        }
    }

    private Task StopAllWatchersAsync() => _fileWatcherManager.StopAllAsync();

    private void OnWatcherError(string folder, ErrorEventArgs e) =>
        // The manager handles restart attempts. Worker just logs and records diagnostics here.
        // Consider direct logger call or add to LoggerDelegates if needed
        LoggerDelegates.ErrorProcessingFileWarning(_logger, $"Watcher error in folder: {folder}", e.GetException());

    private async Task CleanupAsync() {
        await StopAllWatchersAsync();

        _optionsSubscription?.Dispose();

        // Background services (FileDebounceService, FileSenderService) handle their own cleanup
        // through the BackgroundService lifecycle

        _diagnostics.Dispose();
    }

    private void ConfigureLogging(string? logLevelString) {
        // If null, treat as Trace (show everything)
        logLevelString ??= "Trace";
        try {
            Enum.TryParse<LogLevel>(logLevelString, true, out _);
        }
        catch (Exception ex) {
            LoggerDelegates.FailedToConfigureLogging(_logger, "An error occurred while configuring logging.", ex);
        }
    }



    /// <summary>
    /// Get the effective configuration for a file path by merging action settings with global defaults.
    /// Precedence: action-level setting > global default.
    /// </summary>
    private ExternalConfiguration GetConfigForPath(string path) {
        // Find the most specific matching folder (longest path) that is a prefix of the file path
        ExternalConfiguration.WatchedFolderConfig? folder = null;
        string normalized = Path.GetFullPath(path);

        foreach (ExternalConfiguration.WatchedFolderConfig f in CurrentConfig.Folders) {
            if (string.IsNullOrWhiteSpace(f.FolderPath)) continue;
            string folderFull;
            try { folderFull = Path.GetFullPath(f.FolderPath); } catch { continue; }
            if (!normalized.StartsWith(folderFull, StringComparison.OrdinalIgnoreCase)) continue;
            if (folder is null || folderFull.Length > folder.FolderPath.Length) folder = f;
        }

        // If no folder match, return global config as-is
        if (folder is null) return CurrentConfig;

        // Resolve the action configuration
        ExternalConfiguration.ActionConfig? action = null;
        if (!string.IsNullOrWhiteSpace(folder.ActionName) && CurrentConfig.Actions is not null) {
            action = CurrentConfig.Actions.FirstOrDefault(a =>
                string.Equals(a.Name, folder.ActionName, StringComparison.OrdinalIgnoreCase));
        }

        // If no action found, return global config as-is
        if (action is null) return CurrentConfig;

        // Merge action settings with global defaults
        return new ExternalConfiguration {
            // Action-specific settings (REST API)
            ApiEndpoint = action.ApiEndpoint ?? CurrentConfig.ApiEndpoint,
            BearerToken = action.BearerToken ?? CurrentConfig.BearerToken,

            // File processing behavior
            PostFileContents = action.PostFileContents ?? CurrentConfig.PostFileContents,
            MoveProcessedFiles = action.MoveProcessedFiles ?? CurrentConfig.MoveProcessedFiles,
            ProcessedFolder = action.ProcessedFolder ?? CurrentConfig.ProcessedFolder,
            AllowedExtensions = action.AllowedExtensions ?? CurrentConfig.AllowedExtensions,
            ExcludePatterns = action.ExcludePatterns ?? CurrentConfig.ExcludePatterns,
            IncludeSubdirectories = action.IncludeSubdirectories ?? CurrentConfig.IncludeSubdirectories,

            // Timing and retry settings
            DebounceMilliseconds = action.DebounceMilliseconds ?? CurrentConfig.DebounceMilliseconds,
            Retries = action.Retries ?? CurrentConfig.Retries,
            RetryDelayMilliseconds = action.RetryDelayMilliseconds ?? CurrentConfig.RetryDelayMilliseconds,
            WaitForFileReadyMilliseconds = action.WaitForFileReadyMilliseconds ?? CurrentConfig.WaitForFileReadyMilliseconds,

            // Content size settings
            MaxContentBytes = action.MaxContentBytes ?? CurrentConfig.MaxContentBytes,
            StreamingThresholdBytes = action.StreamingThresholdBytes ?? CurrentConfig.StreamingThresholdBytes,
            DiscardZeroByteFiles = action.DiscardZeroByteFiles ?? CurrentConfig.DiscardZeroByteFiles,

            // Circuit breaker settings
            EnableCircuitBreaker = action.EnableCircuitBreaker ?? CurrentConfig.EnableCircuitBreaker,
            CircuitBreakerFailureThreshold = action.CircuitBreakerFailureThreshold ?? CurrentConfig.CircuitBreakerFailureThreshold,
            CircuitBreakerOpenDurationMilliseconds = action.CircuitBreakerOpenDurationMilliseconds ?? CurrentConfig.CircuitBreakerOpenDurationMilliseconds,

            // Global-only settings (never overridden by actions)
            Folders = CurrentConfig.Folders,
            Actions = CurrentConfig.Actions ?? [],
            WatcherMaxRestartAttempts = CurrentConfig.WatcherMaxRestartAttempts,
            WatcherRestartDelayMilliseconds = CurrentConfig.WatcherRestartDelayMilliseconds,
            ChannelCapacity = CurrentConfig.ChannelCapacity,
            MaxParallelSends = CurrentConfig.MaxParallelSends,
            FileWatcherInternalBufferSize = CurrentConfig.FileWatcherInternalBufferSize,
            DiagnosticsUrlPrefix = CurrentConfig.DiagnosticsUrlPrefix,
            DiagnosticsBearerToken = CurrentConfig.DiagnosticsBearerToken,
            Logging = CurrentConfig.Logging
        };
    }

    /// <summary>
    /// Scan configured folders for existing files on startup and enqueue them for processing.
    /// This handles the scenario where files were added while the service was down.
    /// </summary>
    /// <param name="ct"></param>
    private Task EnqueueExistingFilesAsync(CancellationToken ct) {
        try {
            if (CurrentConfig.Folders.Count == 0) {
                // No folders configured, nothing to enqueue
                return Task.CompletedTask;
            }

            foreach (ExternalConfiguration.WatchedFolderConfig folderConfig in CurrentConfig.Folders) {
                string folderPath = folderConfig.FolderPath;
                if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath)) {
                    continue;
                }

                foreach (string filePath in Directory.EnumerateFiles(folderPath, "*", SearchOption.TopDirectoryOnly)) {
                    // Use GetConfigForPath to get merged action + global configuration
                    ExternalConfiguration cfg = GetConfigForPath(filePath);

                    // Exclude files in processed folder
                    string[] pathParts = filePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    if (pathParts.Any(part => part.Equals(cfg.ProcessedFolder, StringComparison.OrdinalIgnoreCase))) {
                        continue;
                    }

                    // Apply file extension filtering
                    if (cfg.AllowedExtensions is { Length: > 0 }) {
                        string extension = Path.GetExtension(filePath);
                        if (!cfg.AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase)) {
                            continue;
                        }
                    }

                    // Apply exclude pattern filtering
                    if (cfg.ExcludePatterns is { Length: > 0 }) {
                        string fileName = Path.GetFileName(filePath);
                        string? matchedPattern = FileSystemPatternMatcher.TryMatchAny(fileName, cfg.ExcludePatterns);
                        if (matchedPattern is not null) {
                            continue;
                        }
                    }

                    // Skip files that have already been posted
                    if (_diagnostics.IsFilePosted(filePath)) {
                        continue;
                    }

                    // Enqueue file for processing via FileDebounceService
                    _debounceService.Schedule(filePath);
                }
            }
        }
        catch (Exception ex) {
            LoggerDelegates.FailedToProcessFile(_logger, ex);
        }

        return Task.CompletedTask;
    }
}
