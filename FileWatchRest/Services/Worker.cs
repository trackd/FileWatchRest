namespace FileWatchRest.Services;

public partial class Worker : BackgroundService
{
    private static readonly Action<ILogger<Worker>, int, Exception?> _loadedConfig =
        LoggerMessage.Define<int>(LogLevel.Information, new EventId(1, "LoadedConfig"), "Loaded external configuration with {FolderCount} folders");
    private static readonly Action<ILogger<Worker>, Exception?> _noFoldersConfigured =
        LoggerMessage.Define(LogLevel.Warning, new EventId(2, "NoFoldersConfigured"), "No folders configured to watch. Update the configuration file in AppData.");
    private static readonly Action<ILogger<Worker>, string, Exception?> _watcherFailedStopping =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(3, "WatcherFailedStopping"), "Watcher for {Folder} failed after max restart attempts, stopping service");
    private static readonly Action<ILogger<Worker>, string, Exception?> _queuedDebouncedTrace =
        LoggerMessage.Define<string>(LogLevel.Trace, new EventId(4, "QueuedDebounced"), "Queued debounced enqueue for {Path}");
    private static readonly Action<ILogger<Worker>, string, long?, bool, Exception?> _createdNotificationDebug =
        LoggerMessage.Define<string, long?, bool>(LogLevel.Debug, new EventId(5, "CreatedNotification"), "Created notification for {Path}: FileSize={FileSize}, HasContent={HasContent}");
    private static readonly Action<ILogger<Worker>, string, Exception?> _failedSchedulingDebounce =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(6, "FailedSchedulingDebounce"), "Failed scheduling debounced enqueue for {Path}");
    private static readonly Action<ILogger<Worker>, string, Exception?> _errorProcessingFileWarning =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(7, "ErrorProcessingFile"), "Error processing file {Path}");
    private static readonly Action<ILogger<Worker>, string, Exception?> _apiEndpointNotConfigured =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(8, "ApiEndpointNotConfigured"), "ApiEndpoint not configured. Skipping file {Path}");
    private static readonly Action<ILogger<Worker>, Exception?> _failedToProcessFile =
        LoggerMessage.Define(LogLevel.Error, new EventId(9, "FailedToProcessFile"), "Failed to process file");
    private static readonly Action<ILogger<Worker>, string, Exception?> _ignoredFileInProcessed =
        LoggerMessage.Define<string>(LogLevel.Debug, new EventId(10, "IgnoredInProcessed"), "Ignoring file in processed folder: {Path}");
    private static readonly Action<ILogger<Worker>, string, bool, bool, Exception?> _processingFileTrace =
        LoggerMessage.Define<string, bool, bool>(LogLevel.Trace, new EventId(11, "ProcessingFile"), "Processing file {Path}, PostFileContents: {PostContents}, MoveAfterProcessing: {MoveFiles}");
    private static readonly Action<ILogger<Worker>, string, long, Exception?> _fileExceedsMaxContentWarning =
        LoggerMessage.Define<string, long>(LogLevel.Warning, new EventId(12, "FileExceedsMaxContent"), "File {Path} exceeds MaxContentBytes ({Limit} bytes); sending metadata only.");
    private static readonly Action<ILogger<Worker>, string, Exception?> _failedReadFileWarning =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(13, "FailedReadFile"), "Failed to read file contents for {Path}");
    private static readonly Action<ILogger<Worker>, string, string, Exception?> _attachingAuthDebug =
        LoggerMessage.Define<string, string>(LogLevel.Debug, new EventId(14, "AttachingAuth"), "Attaching Authorization header for endpoint {Endpoint}. Token preview: {TokenPreview}");
    private static readonly Action<ILogger<Worker>, string, string, Exception?> _circuitOpenSkipWarning =
        LoggerMessage.Define<string, string>(LogLevel.Warning, new EventId(15, "CircuitOpenSkip"), "Circuit breaker is open for endpoint {Endpoint}; skipping post for {Path}");
    private static readonly Action<ILogger<Worker>, string, int, long, int, Exception?> _successPostedInfo =
        LoggerMessage.Define<string, int, long, int>(LogLevel.Information, new EventId(16, "PostedSuccess"), "Successfully posted file {Path} with StatusCode {StatusCode} in {TotalMs}ms (attempts={Attempts})");
    private static readonly Action<ILogger<Worker>, string, int, Exception?> _failedPostError =
        LoggerMessage.Define<string, int>(LogLevel.Error, new EventId(17, "FailedPost"), "Failed to post file {Path} after {Attempts} attempts");
    private static readonly Action<ILogger<Worker>, string, string, Exception?> _movedProcessedInfo =
        LoggerMessage.Define<string, string>(LogLevel.Information, new EventId(18, "MovedProcessed"), "Moved processed file {From} to {To}");
    private static readonly Action<ILogger<Worker>, string, Exception?> _failedMoveWarning =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(19, "FailedMoveProcessed"), "Failed to move processed file {Path}");
    private static readonly Action<ILogger<Worker>, Exception?> _configReloadError =
        LoggerMessage.Define(LogLevel.Error, new EventId(20, "ConfigReloadFailed"), "Failed to reload configuration");
    private static readonly Action<ILogger<Worker>, Exception?> _configChangedInfo =
        LoggerMessage.Define(LogLevel.Information, new EventId(21, "ConfigChanged"), "Configuration changed, reloading watchers");
    private static readonly Action<ILogger<Worker>, Exception?> _configReloadedInfo =
        LoggerMessage.Define(LogLevel.Information, new EventId(22, "ConfigReloaded"), "Configuration reload completed");
    private static readonly Action<ILogger<Worker>, LogLevel, Exception?> _loggingConfiguredInfo =
        LoggerMessage.Define<LogLevel>(LogLevel.Information, new EventId(23, "LoggingConfigured"), "Logging level configured to {LogLevel}. Note: Restart service for log level changes to take full effect");
    private static readonly Action<ILogger<Worker>, string, Exception?> _invalidLogLevelWarning =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(24, "InvalidLogLevel"), "Invalid LogLevel '{LogLevel}' in configuration. Valid values: Trace, Debug, Information, Warning, Error, Critical, None");
    private static readonly Action<ILogger<Worker>, string, Exception?> _failedConfigureLoggingWarning =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(25, "FailedConfigureLogging"), "Failed to configure logging level from '{LogLevel}', using Information");
    private static readonly Action<ILogger<Worker>, string, string, Exception?> _requestCorrelationDebug =
        LoggerMessage.Define<string, string>(LogLevel.Debug, new EventId(34, "RequestCorrelation"), "Request {RequestId} for Path {Path}");
    private static readonly Action<ILogger<Worker>, Exception?> _noFoldersScanDebug =
        LoggerMessage.Define(LogLevel.Debug, new EventId(26, "NoFoldersScan"), "No folders configured - skipping existing-files scan");
    private static readonly Action<ILogger<Worker>, string, Exception?> _configuredFolderMissingWarning =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(27, "ConfiguredFolderMissing"), "Configured folder does not exist: {Folder}");
    private static readonly Action<ILogger<Worker>, string, Exception?> _failedQueueExistingFileWarning =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(28, "FailedQueueExistingFile"), "Failed to queue existing file {Path} - channel full");
    private static readonly Action<ILogger<Worker>, int, string, Exception?> _enqueuedExistingFilesInfo =
        LoggerMessage.Define<int, string>(LogLevel.Information, new EventId(29, "EnqueuedExistingFiles"), "Enqueued {Count} existing files from {Folder}");
    private static readonly Action<ILogger<Worker>, int, string, Exception?> _completedEnqueueInfo =
        LoggerMessage.Define<int, string>(LogLevel.Information, new EventId(30, "CompletedEnqueue"), "Completed enqueueing {Count} existing files from {Folder}");
    private static readonly Action<ILogger<Worker>, string, Exception?> _failedScanningFolderWarning =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(31, "FailedScanningFolder"), "Failed scanning folder {Folder} for existing files");
    private static readonly Action<ILogger<Worker>, Exception?> _unexpectedErrorScanningFoldersWarning =
        LoggerMessage.Define(LogLevel.Warning, new EventId(32, "UnexpectedErrorScanningFolders"), "Unexpected error scanning folders for existing files");
    private static readonly Action<ILogger<Worker>, string, Exception?> _watcherErrorWarning =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(33, "WatcherErrorWorker"), "FileSystemWatcher error for folder {Folder}");
    private static readonly Action<ILogger<Worker>, string, int, Exception?> _waitingForFileReady =
        LoggerMessage.Define<string, int>(LogLevel.Debug, new EventId(35, "WaitingForFileReady"), "Waiting up to {WaitMs}ms for file {Path} to become ready and non-empty");
    private static readonly Action<ILogger<Worker>, string, int, Exception?> _fileStillEmptyWarning =
        LoggerMessage.Define<string, int>(LogLevel.Warning, new EventId(36, "FileStillEmpty"), "File {Path} remained empty after waiting {WaitMs}ms");
    private static readonly Action<ILogger<Worker>, string, Exception?> _skippingZeroLengthFile =
        LoggerMessage.Define<string>(LogLevel.Information, new EventId(37, "SkippingZeroLengthFile"), "Skipping zero-length file {Path} after waiting for content");

    private readonly ILogger<Worker> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ConfigurationService _configService;
    private readonly FileWatcherManager _fileWatcherManager;
    private readonly ConcurrentDictionary<string, DateTime> _pending = new(StringComparer.OrdinalIgnoreCase);
    private Task? _debounceSchedulerTask;
    private Channel<string>? _sendChannel;
    private List<Task>? _senderTasks;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly DiagnosticsService _diagnostics;
    private readonly SemaphoreSlim _configReloadLock = new(1, 1);
    private readonly IResilienceService _resilienceService;
    private readonly IOptionsMonitor<ExternalConfiguration> _optionsMonitor;
    private IDisposable? _optionsSubscription;

    // Expose current configuration for tests and controlled updates
    internal ExternalConfiguration CurrentConfig { get => _currentConfig; set => _currentConfig = value; }

    private ExternalConfiguration _currentConfig = new();
    private LogLevel _configuredLogLevel = LogLevel.Information;

    public Worker(
        ILogger<Worker> logger,
        IHttpClientFactory httpClientFactory,
        IHostApplicationLifetime lifetime,
        DiagnosticsService diagnostics,
        ConfigurationService configService,
        FileWatcherManager fileWatcherManager,
        IResilienceService resilienceService,
        IOptionsMonitor<ExternalConfiguration> optionsMonitor)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _lifetime = lifetime;
        _diagnostics = diagnostics;
        _configService = configService;
        _fileWatcherManager = fileWatcherManager;
        _resilienceService = resilienceService;
        _optionsMonitor = optionsMonitor;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Load external configuration
        _currentConfig = await _configService.LoadConfigurationAsync(stoppingToken);

        // Apply logging configuration
        ConfigureLogging(_currentConfig.Logging?.LogLevel);

        _loadedConfig(_logger, _currentConfig.Folders.Length, null);

    // Configure diagnostics access token and start HTTP server
    _diagnostics.SetBearerToken(_currentConfig.DiagnosticsBearerToken);
    _diagnostics.StartHttpServer(_currentConfig.DiagnosticsUrlPrefix);

        // Start watching for configuration changes via options monitor (monitor registers the file watcher)
        _optionsSubscription = _optionsMonitor.OnChange(async (newCfg, name) => await OnConfigurationChanged(newCfg));

        if (_currentConfig.Folders.Length == 0)
        {
            _noFoldersConfigured(_logger, null);
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            return;
        }

        await StartWatchingFoldersAsync(_currentConfig.Folders);

        // Initialize send channel and sender tasks
        _sendChannel = Channel.CreateBounded<string>(_currentConfig.ChannelCapacity);
        _senderTasks = new List<Task>();
         for (int i = 0; i < Math.Max(1, _currentConfig.MaxParallelSends); i++)
         {
             _senderTasks.Add(Task.Run(() => SenderLoopAsync(_sendChannel.Reader, stoppingToken), stoppingToken));
         }

        // Start centralized debounce scheduler
        _debounceSchedulerTask = Task.Run(() => DebounceSchedulerLoop(stoppingToken), stoppingToken);

        // Enqueue existing files that were added while service was down
        await EnqueueExistingFilesAsync(stoppingToken);

        // Block until cancellation; debounced enqueue tasks will drive processing
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Exit gracefully on shutdown
        }

        // Cleanup
        await CleanupAsync();
    }

    private Task StartWatchingFoldersAsync(string[] folders)
    {
        return _fileWatcherManager.StartWatchingAsync(folders, _currentConfig, OnFileChanged, OnWatcherError, folder =>
        {
            // Exceeded restart attempts -> stop application
            if (_logger.IsEnabled(LogLevel.Error))
            {
                _watcherFailedStopping(_logger, folder, null);
            }
            _lifetime.StopApplication();
        });
    }

    private void OnFileChanged(object? sender, FileSystemEventArgs e)
    {
        // Accept Created, Changed, and Renamed events
        // Renamed events occur when files are moved into the watched folder
        if (e.ChangeType is not (WatcherChangeTypes.Created or WatcherChangeTypes.Changed or WatcherChangeTypes.Renamed))
            return;

        // Exclude files in the processed folder to prevent infinite loops
        var pathParts = e.FullPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (pathParts.Any(part => part.Equals(_currentConfig.ProcessedFolder, StringComparison.OrdinalIgnoreCase)))
        {
            _ignoredFileInProcessed(_logger, e.FullPath, null);
            return;
        }

        // Apply file extension filtering
        if (_currentConfig.AllowedExtensions.Length > 0)
        {
            var extension = Path.GetExtension(e.FullPath);
            if (!_currentConfig.AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                return;
            }
        }

        // Apply exclude pattern filtering
        if (_currentConfig.ExcludePatterns.Length > 0)
        {
            var fileName = Path.GetFileName(e.FullPath);
            foreach (var pattern in _currentConfig.ExcludePatterns)
            {
                if (MatchesPattern(fileName, pattern))
                {
                    return;
                }
            }
        }

        // Schedule debounced enqueue for this path
        ScheduleEnqueueAfterDebounce(e.FullPath, _currentConfig.DebounceMilliseconds);

        _queuedDebouncedTrace(_logger, e.FullPath, null);

        // Fast-path for zero debounce - short-circuit scheduling and try to write immediately
        if (_currentConfig.DebounceMilliseconds <= 0 && _sendChannel is not null)
        {
            _sendChannel.Writer.TryWrite(e.FullPath);
        }
    }

    private void ScheduleEnqueueAfterDebounce(string path, int debounceMs)
    {
        // Update pending timestamp for this path (UTC to avoid timezone issues)
        _pending.AddOrUpdate(path, DateTime.UtcNow, (_, __) => DateTime.UtcNow);
        return;
    }

    private async Task DebounceSchedulerLoop(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    if (_pending.IsEmpty)
                    {
                        await Task.Delay(100, ct);
                        continue;
                    }

                    var now = DateTime.Now;
                    var due = new List<string>();
                    foreach (var kv in _pending)
                    {
                        if ((now - kv.Value).TotalMilliseconds >= _currentConfig.DebounceMilliseconds)
                        {
                            if (_pending.TryRemove(kv.Key, out _)) due.Add(kv.Key);
                        }
                    }

                    if (due.Count > 0 && _sendChannel is not null)
                    {
                        foreach (var p in due)
                        {
                            // Backpressure policy: try immediate write, otherwise attempt to write with short timeout
                            if (!_sendChannel.Writer.TryWrite(p))
                            {
                                var writeTask = _sendChannel.Writer.WriteAsync(p, ct).AsTask();
                                var completed = await Task.WhenAny(writeTask, Task.Delay(1000, ct));
                                if (completed != writeTask)
                                {
                                    // Timed out; drop and log
                                    _failedQueueExistingFileWarning(_logger, p, null);
                                }
                            }
                        }
                    }

                    await Task.Delay(100, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    if (_logger.IsEnabled(LogLevel.Warning)) _failedSchedulingDebounce(_logger, "<scheduler>", ex);
                    await Task.Delay(1000, ct);
                }
            }
        }
        catch { /* swallow */ }
    }

    private async Task SenderLoopAsync(ChannelReader<string> reader, CancellationToken ct)
    {
        await foreach (var path in reader.ReadAllAsync(ct))
        {
            try
            {
                if (_currentConfig.WaitForFileReadyMilliseconds > 0)
                {
                    var ready = await WaitForFileReadyAsync(path, ct);
                    if (!ready)
                    {
                        // Configured to discard zero-length files and file remained empty - skip processing
                        _diagnostics.RecordFileEvent(path, false, null);
                        continue;
                    }
                }

                var notification = await CreateNotificationAsync(path, ct);
                _createdNotificationDebug(_logger, path, notification.FileSize, !string.IsNullOrEmpty(notification.Content), null);
                var success = await SendNotificationAsync(notification, ct);

                if (success && _currentConfig.MoveProcessedFiles)
                {
                    await MoveToProcessedFolderAsync(path, ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _errorProcessingFileWarning(_logger, path, ex);
                _diagnostics.RecordFileEvent(path, false, null);
            }
        }
    }

    internal async Task<bool> WaitForFileReadyAsync(string path, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var waited = false;
        var waitMs = Math.Max(0, _currentConfig.WaitForFileReadyMilliseconds);
        if (_logger.IsEnabled(LogLevel.Debug)) _waitingForFileReady(_logger, path, waitMs, null);

        while (sw.ElapsedMilliseconds < waitMs)
        {
            try
            {
                // Try to open for read to ensure the writer has released any exclusive locks
                using var fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);

                if (!_currentConfig.PostFileContents)
                {
                    // We don't need content - file is considered ready if it can be opened
                    return true;
                }

                // If PostFileContents is configured, wait until file length is non-zero
                if (fs.Length > 0)
                {
                    return true;
                }

                // File exists and is readable but empty; continue waiting
                waited = true;
            }
            catch
            {
                // File not ready (locked/not present) - keep waiting
            }

            try { await Task.Delay(50, ct); } catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
        }

        if (waited)
        {
            _fileStillEmptyWarning(_logger, path, waitMs, null);
        }

        // If configured to discard zero-length files, indicate caller to skip processing
        if (_currentConfig.DiscardZeroByteFiles)
        {
            _skippingZeroLengthFile(_logger, path, null);
            return false;
        }

        // Otherwise, proceed (will process zero-length file)
        return true;
    }

    private async Task ProcessFileAsync(string path, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_currentConfig.ApiEndpoint))
        {
            _apiEndpointNotConfigured(_logger, path, null);
            return;
        }

        try
        {
            _processingFileTrace(_logger, path, _currentConfig.PostFileContents, _currentConfig.MoveProcessedFiles, null);

            var notification = await CreateNotificationAsync(path, ct);
            _createdNotificationDebug(_logger, path, notification.FileSize, !string.IsNullOrEmpty(notification.Content), null);
            var success = await SendNotificationAsync(notification, ct);

            if (success && _currentConfig.MoveProcessedFiles)
            {
                await MoveToProcessedFolderAsync(path, ct);
            }
        }
        catch (Exception ex)
        {
            _failedToProcessFile(_logger, ex);
            _diagnostics.RecordFileEvent(path, false, null);
        }
    }

    internal async Task<FileNotification> CreateNotificationAsync(string path, CancellationToken ct)
    {
        var fileInfo = new FileInfo(path);
        var notification = new FileNotification
        {
            Path = path,
            FileSize = fileInfo.Length,
            LastWriteTime = fileInfo.LastWriteTime
        };

        if (_currentConfig.PostFileContents)
        {
            try
            {
                // Enforce a maximum content size to avoid large memory allocations
                if (fileInfo.Length <= _currentConfig.MaxContentBytes)
                {
                    notification.Content = await File.ReadAllTextAsync(path, ct);
                }
                else
                {
                    _fileExceedsMaxContentWarning(_logger, path, _currentConfig.MaxContentBytes, null);
                    notification.Content = null; // Do not include contents that exceed the configured limit
                }
            }
            catch (Exception ex)
            {
                _failedReadFileWarning(_logger, path, ex);
                // Continue without contents
            }
        }

        return notification;
    }

    // Helper extracted from SendNotificationAsync to decide whether to use multipart streaming for a given notification.
    internal bool ShouldUseStreamingUpload(FileNotification notification)
    {
        if (!_currentConfig.PostFileContents) return false;
        if (!notification.FileSize.HasValue) return false;
        var size = notification.FileSize.Value;
        var threshold = Math.Max(0, _currentConfig.StreamingThresholdBytes);
        return size > threshold && size <= _currentConfig.MaxContentBytes;
    }

    internal async Task<bool> SendNotificationAsync(FileNotification notification, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_currentConfig.ApiEndpoint))
        {
            _apiEndpointNotConfigured(_logger, notification.Path, null);
            return false;
        }

        using var fileClient = _httpClientFactory.CreateClient("fileApi");

        // Prepare a factory to create a fresh HttpRequestMessage for each attempt so streaming content is created per attempt.
    Func<CancellationToken, Task<HttpRequestMessage>> requestFactory = ct =>
        {
            // Decide sending strategy: use streaming upload when appropriate
            if (ShouldUseStreamingUpload(notification))
            {
                var metadataObj = new UploadMetadata
                {
                    Path = notification.Path,
                    FileSize = notification.FileSize,
                    LastWriteTime = notification.LastWriteTime
                };

                var multipart = new MultipartFormDataContent();
                var metadataJson = JsonSerializer.Serialize(metadataObj, MyJsonContext.Default.UploadMetadata);
                var metadataContent = new StringContent(metadataJson, Encoding.UTF8);
                metadataContent.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                multipart.Add(metadataContent, "metadata");

                // Open file stream for streaming upload (stream will be disposed when HttpRequestMessage is disposed by the resilience service)
                var fs = File.Open(notification.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
                var streamContent = new StreamContent(fs);
                streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                multipart.Add(streamContent, "file", Path.GetFileName(notification.Path));

                var reqMsg = new HttpRequestMessage(HttpMethod.Post, _currentConfig.ApiEndpoint) { Content = multipart };
                if (!string.IsNullOrWhiteSpace(_currentConfig.BearerToken))
                {
                    var token = _currentConfig.BearerToken;
                    if (token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) token = token[7..].Trim();
                    reqMsg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                    _attachingAuthDebug(_logger, _currentConfig.ApiEndpoint ?? string.Empty, token, null);
                }
                // Add a short-lived correlation id to help trace the request across logs and diagnostics
                var requestId = Guid.NewGuid().ToString("N");
                if (!reqMsg.Headers.Contains("X-Request-Id")) reqMsg.Headers.Add("X-Request-Id", requestId);
                _requestCorrelationDebug(_logger, requestId, notification.Path, null);
                return Task.FromResult(reqMsg);
            }
            else
            {
                var bytes = JsonSerializer.SerializeToUtf8Bytes(notification, MyJsonContext.Default.FileNotification);
                var ms = new MemoryStream(bytes, writable: false);
                var content = new StreamContent(ms);
                content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                var reqMsg = new HttpRequestMessage(HttpMethod.Post, _currentConfig.ApiEndpoint) { Content = content };
                if (!string.IsNullOrWhiteSpace(_currentConfig.BearerToken))
                {
                    var token = _currentConfig.BearerToken;
                    if (token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) token = token[7..].Trim();
                    reqMsg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                    _attachingAuthDebug(_logger, _currentConfig.ApiEndpoint ?? string.Empty, token, null);
                }
                // Correlate request with a unique id for tracing
                var requestId = Guid.NewGuid().ToString("N");
                if (!reqMsg.Headers.Contains("X-Request-Id")) reqMsg.Headers.Add("X-Request-Id", requestId);
                _requestCorrelationDebug(_logger, requestId, notification.Path, null);
                return Task.FromResult(reqMsg);
            }
        };

         var endpointKey = string.IsNullOrWhiteSpace(_currentConfig.ApiEndpoint) ? string.Empty : _currentConfig.ApiEndpoint;

         var result = await _resilienceService.SendWithRetriesAsync(requestFactory, fileClient, endpointKey, _currentConfig, ct);

         // Diagnostics and logging for this file are recorded here using the worker's notification path
         _diagnostics.RecordFileEvent(notification.Path, result.Success, result.LastStatusCode);

         if (result.ShortCircuited)
        {
            _circuitOpenSkipWarning(_logger, endpointKey ?? string.Empty, notification.Path, null);
            return false;
        }

        if (result.Success)
        {
            _successPostedInfo(_logger, notification.Path, result.LastStatusCode ?? 0, result.TotalElapsedMs, result.Attempts, null);
            return true;
        }

        if (result.LastException is not null)
        {
            _failedPostError(_logger, notification.Path, result.Attempts, result.LastException);
        }

        return false;
    }

    private Task MoveToProcessedFolderAsync(string filePath, CancellationToken ct)
    {
        try
        {
            var directory = Path.GetDirectoryName(filePath)!;
            var processedDir = Path.Combine(directory, _currentConfig.ProcessedFolder);

            Directory.CreateDirectory(processedDir);

            var fileName = Path.GetFileName(filePath);
            var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
            var extension = Path.GetExtension(fileName);

            // Add datetime prefix for uniqueness and traceability
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
            var processedFileName = $"{timestamp}_{nameWithoutExt}{extension}";
            var processedPath = Path.Combine(processedDir, processedFileName);

            // Handle extremely rare case where files are processed at exact same millisecond
            int counter = 1;
            while (File.Exists(processedPath))
            {
                processedFileName = $"{timestamp}_{nameWithoutExt}_{counter}{extension}";
                processedPath = Path.Combine(processedDir, processedFileName);
                counter++;
            }

            File.Move(filePath, processedPath);
            _movedProcessedInfo(_logger, filePath, processedPath, null);
        }
        catch (Exception ex)
        {
            _failedMoveWarning(_logger, filePath, ex);
        }

        return Task.CompletedTask;
    }

    private async Task OnConfigurationChanged(ExternalConfiguration newConfig)
    {
        if (!await _configReloadLock.WaitAsync(0))
            return; // Already reloading

        try
        {
            _configChangedInfo(_logger, null);

            // Stop current watchers via manager
            await _fileWatcherManager.StopAllAsync();

            // Start new watchers
            if (newConfig.Folders.Length > 0)
            {
                await StartWatchingFoldersAsync(newConfig.Folders);
            }

            // Apply logging configuration from the reloaded config
            ConfigureLogging(newConfig.Logging?.LogLevel);
            // Update diagnostics bearer token in case it changed
            _diagnostics.SetBearerToken(newConfig.DiagnosticsBearerToken);

            _configReloadedInfo(_logger, null);
        }
        catch (Exception ex)
        {
            _configReloadError(_logger, ex);
        }
        finally
        {
            _configReloadLock.Release();
        }
    }

    private Task StopAllWatchersAsync()
    {
        return _fileWatcherManager.StopAllAsync();
    }

    private void OnWatcherError(string folder, ErrorEventArgs e)
    {
        // The manager handles restart attempts. Worker just logs and records diagnostics here.
        _watcherErrorWarning(_logger, folder, e.GetException());
        _diagnostics.IncrementRestart(folder);
    }

    private async Task CleanupAsync()
    {
        await StopAllWatchersAsync();

        _optionsSubscription?.Dispose();

        // Wait for debounce scheduler to finish (it will observe the stopping token cancellation)
        if (_debounceSchedulerTask is not null)
        {
            try { await Task.WhenAny(_debounceSchedulerTask, Task.Delay(1000)); } catch { }
        }
        _pending.Clear();

        if (_sendChannel is not null)
        {
            _sendChannel.Writer.Complete();
            if (_senderTasks is not null)
            {
                await Task.WhenAll(_senderTasks.Where(t => !t.IsCompleted));
            }
        }

        _configService.Dispose();
        _diagnostics.Dispose();
    }

    /// <summary>
    /// Check if a filename matches a wildcard pattern.
    /// Supports * (any characters) and ? (single character) wildcards.
    /// </summary>
    private static bool MatchesPattern(string fileName, string pattern)
    {
        if (string.IsNullOrEmpty(pattern))
            return false;

        // Convert wildcard pattern to regex
        // Escape regex special characters except * and ?
        var regexPattern = "^" + Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";

        return Regex.IsMatch(fileName, regexPattern, RegexOptions.IgnoreCase);
    }

    private void ConfigureLogging(string? logLevelString)
    {
        // If null, treat as Information
        logLevelString ??= "Information";
        try
        {
            if (Enum.TryParse<LogLevel>(logLevelString, true, out var configuredLevel))
            {
                _loggingConfiguredInfo(_logger, configuredLevel, null);

                // Store the configured level for conditional logging checks
                _configuredLogLevel = configuredLevel;
            }
            else
            {
                _invalidLogLevelWarning(_logger, logLevelString, null);
                _configuredLogLevel = LogLevel.Information;
            }
        }
        catch (Exception ex)
        {
            _failedConfigureLoggingWarning(_logger, logLevelString, ex);
            _configuredLogLevel = LogLevel.Information;
        }
    }

    /// <summary>
    /// Scan configured folders for existing files on startup and enqueue them for processing.
    /// This handles the scenario where files were added while the service was down.
    /// </summary>
    private Task EnqueueExistingFilesAsync(CancellationToken ct)
    {
        try
        {
            if (_currentConfig.Folders.Length == 0)
            {
                _noFoldersScanDebug(_logger, null);
                return Task.CompletedTask;
            }

            foreach (var folder in _currentConfig.Folders)
            {
                if (ct.IsCancellationRequested)
                    break;

                try
                {
                    if (!Directory.Exists(folder))
                    {
                        _configuredFolderMissingWarning(_logger, folder, null);
                        continue;
                    }

                    var searchOption = _currentConfig.IncludeSubdirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

                    // Enumerate files; avoid loading large lists into memory by streaming
                    var fileEnum = Directory.EnumerateFiles(folder, "*", searchOption);
                    int enqueued = 0;
                    foreach (var filePath in fileEnum)
                    {
                        if (ct.IsCancellationRequested)
                            break;

                        // Exclude processed folder
                        var pathParts = filePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                        if (pathParts.Any(part => part.Equals(_currentConfig.ProcessedFolder, StringComparison.OrdinalIgnoreCase)))
                            continue;

                        // Extension filtering
                        if (_currentConfig.AllowedExtensions.Length > 0)
                        {
                            var ext = Path.GetExtension(filePath);
                            if (!_currentConfig.AllowedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
                                continue;
                        }

                        // Exclude pattern filtering
                        if (_currentConfig.ExcludePatterns.Length > 0)
                        {
                            var fileName = Path.GetFileName(filePath);
                            bool excluded = false;
                            foreach (var pattern in _currentConfig.ExcludePatterns)
                            {
                                if (MatchesPattern(fileName, pattern))
                                {
                                    excluded = true;
                                    break;
                                }
                            }
                            if (excluded)
                                continue;
                        }

                        // Schedule debounced enqueue for every existing file
                        ScheduleEnqueueAfterDebounce(filePath, _currentConfig.DebounceMilliseconds);

                        if (_currentConfig.DebounceMilliseconds <= 0 && _sendChannel is not null)
                        {
                            if (!_sendChannel.Writer.TryWrite(filePath))
                            {
                                _failedQueueExistingFileWarning(_logger, filePath, null);
                            }
                        }
                        _diagnostics.IncrementEnqueued();

                        enqueued++;

                        // Avoid overly long startup scans; log progress periodically
                        if ((enqueued % 500) == 0)
                        {
                            _enqueuedExistingFilesInfo(_logger, enqueued, folder, null);
                        }
                    }

                    _completedEnqueueInfo(_logger, enqueued, folder, null);
                }
                catch (Exception ex)
                {
                    _failedScanningFolderWarning(_logger, folder, ex);
                }
            }
        }
        catch (Exception ex)
        {
            _unexpectedErrorScanningFoldersWarning(_logger, ex);
        }

        return Task.CompletedTask;
    }
    /// <summary>
    /// Checks if the given fileName matches any of the exclude patterns (supports * and ? wildcards).
    /// </summary>
    /// <param name="fileName">The file name to check.</param>
    /// <param name="excludePatterns">A list of wildcard patterns to match against.</param>
    /// <returns>True if the fileName matches any pattern; otherwise, false.</returns>
    public static bool MatchesExcludePattern(string fileName, List<string>? excludePatterns)
    {
        if (excludePatterns is null || excludePatterns.Count == 0)
            return false;
        foreach (var pattern in excludePatterns)
        {
            if (string.IsNullOrWhiteSpace(pattern))
                continue;
            if (WildcardMatch(fileName, pattern))
                return true;
        }
        return false;
    }

    // Simple wildcard matcher supporting * and ?
    private static bool WildcardMatch(string input, string pattern)
    {
        if (pattern == "*") return true;
        int i = 0, j = 0, starIdx = -1, match = 0;
        while (i < input.Length)
        {
            if (j < pattern.Length && (pattern[j] == '?' || char.ToLowerInvariant(pattern[j]) == char.ToLowerInvariant(input[i])))
            {
                i++; j++;
            }
            else if (j < pattern.Length && pattern[j] == '*')
            {
                starIdx = j;
                match = i;
                j++;
            }
            else if (starIdx != -1)
            {
                j = starIdx + 1;
                match++;
                i = match;
            }
            else
            {
                return false;
            }
        }
        while (j < pattern.Length && pattern[j] == '*')
            j++;
        return j == pattern.Length;
    }
}
