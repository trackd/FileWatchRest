using System.Net.Http.Headers;

namespace FileWatchRest.Services;

public partial class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ConfigurationService _configService;
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly ConcurrentDictionary<string, DateTime> _pending = new();
    private Channel<string>? _sendChannel;
    private List<Task>? _senderTasks;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly DiagnosticsService _diagnostics;
    private readonly object _watchersLock = new();
    private readonly SemaphoreSlim _configReloadLock = new(1, 1);
    private readonly ConcurrentDictionary<string, int> _watcherRestartAttempts = new();
    // Circuit breaker state per endpoint
    private sealed class CircuitState
    {
        public int Failures;
        public DateTimeOffset? OpenUntil;
        public readonly object Lock = new();
    }
    private readonly ConcurrentDictionary<string, CircuitState> _circuitStates = new();

    private ExternalConfiguration _currentConfig = new();
    private LogLevel _configuredLogLevel = LogLevel.Information;

    // Expose current configuration for tests and controlled updates
    internal ExternalConfiguration CurrentConfig { get => _currentConfig; set => _currentConfig = value; }

    public Worker(
        ILogger<Worker> logger,
        IHttpClientFactory httpClientFactory,
        IHostApplicationLifetime lifetime,
        DiagnosticsService diagnostics,
        ConfigurationService configService)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _lifetime = lifetime;
        _diagnostics = diagnostics;
        _configService = configService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Load external configuration
        _currentConfig = await _configService.LoadConfigurationAsync(stoppingToken);

        // Apply logging configuration
        ConfigureLogging(_currentConfig.Logging?.LogLevel);

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation("Loaded external configuration with {FolderCount} folders", _currentConfig.Folders.Length);
        }

        // Start diagnostics HTTP server
        _diagnostics.StartHttpServer(_currentConfig.DiagnosticsUrlPrefix);

        // Start watching for configuration changes
        _configService.StartWatching(OnConfigurationChanged);

        if (_currentConfig.Folders.Length == 0)
        {
            _logger.LogWarning("No folders configured to watch. Update the configuration file in AppData.");
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            return;
        }

        await StartWatchingFoldersAsync(_currentConfig.Folders);

        // Initialize send channel and sender tasks
        _sendChannel = Channel.CreateBounded<string>(_currentConfig.ChannelCapacity);
        _senderTasks = [];
        for (int i = 0; i < Math.Max(1, _currentConfig.MaxParallelSends); i++)
        {
            _senderTasks.Add(Task.Run(() => SenderLoopAsync(_sendChannel.Reader, stoppingToken), stoppingToken));
        }

        // Enqueue existing files that were added while service was down
        await EnqueueExistingFilesAsync(stoppingToken);

        // Main debounce loop
        await MainDebounceLoopAsync(stoppingToken);

        // Cleanup
        await CleanupAsync();
    }

    private async Task MainDebounceLoopAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTime.Now;
                var toSend = new List<string>();

                foreach (var (path, timestamp) in _pending)
                {
                    if ((now - timestamp).TotalMilliseconds >= _currentConfig.DebounceMilliseconds)
                    {
                        if (_pending.TryRemove(path, out _))
                        {
                            toSend.Add(path);
                        }
                    }
                }

                if (_sendChannel is not null)
                {
                    foreach (var path in toSend)
                    {
                        if (!_sendChannel.Writer.TryWrite(path))
                        {
                            await _sendChannel.Writer.WriteAsync(path, stoppingToken);
                        }
                    }
                }

                await Task.Delay(50, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in main debounce loop");
                await Task.Delay(1000, stoppingToken);
            }
        }
    }

    private Task StartWatchingFoldersAsync(string[] folders)
    {
        foreach (var folder in folders)
        {
            try
            {
                var watcher = new FileSystemWatcher(folder)
                {
                    InternalBufferSize = Math.Max(4 * 1024, _currentConfig.FileWatcherInternalBufferSize),
                    IncludeSubdirectories = _currentConfig.IncludeSubdirectories,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime | NotifyFilters.LastWrite
                };

                // Set file filter if extensions are specified
                if (_currentConfig.AllowedExtensions.Length > 0)
                {
                    watcher.Filter = "*.*"; // We'll filter in the event handler for multiple extensions
                }

                watcher.Created += OnFileChanged;
                watcher.Changed += OnFileChanged;
                watcher.Error += (s, e) => OnWatcherError(folder, e);
                watcher.EnableRaisingEvents = true;

                lock (_watchersLock)
                {
                    _watchers.Add(watcher);
                }

                _diagnostics.RegisterWatcher(folder);
                if (_logger.IsEnabled(LogLevel.Information))
                {
                    _logger.LogInformation("Watching folder: {Folder}", folder);
                }
            }
            catch (Exception ex)
            {
                if (_logger.IsEnabled(LogLevel.Error))
                {
                    _logger.LogError(ex, "Failed to watch folder {Folder}", folder);
                }
            }
        }

        return Task.CompletedTask;
    }

    private void OnFileChanged(object? sender, FileSystemEventArgs e)
    {
        if (e.ChangeType is not (WatcherChangeTypes.Created or WatcherChangeTypes.Changed))
            return;

        // Exclude files in the processed folder to prevent infinite loops
        // Check if the file path contains the processed folder name as a directory component
        var pathParts = e.FullPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (pathParts.Any(part => part.Equals(_currentConfig.ProcessedFolder, StringComparison.OrdinalIgnoreCase)))
        {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("Ignoring file in processed folder: {Path}", e.FullPath);
            }
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

        _pending[e.FullPath] = DateTime.Now;

        if (_logger.IsEnabled(LogLevel.Trace))
        {
            _logger.LogTrace("Queued file: {Path}", e.FullPath);
        }

        // Fast-path for zero debounce
        if (_currentConfig.DebounceMilliseconds <= 0 && _sendChannel is not null)
        {
            _sendChannel.Writer.TryWrite(e.FullPath);
        }
    }

    private async Task SenderLoopAsync(ChannelReader<string> reader, CancellationToken ct)
    {
        await foreach (var path in reader.ReadAllAsync(ct))
        {
            try
            {
                if (_currentConfig.WaitForFileReadyMilliseconds > 0)
                {
                    await WaitForFileReadyAsync(path, ct);
                }

                var notification = await CreateNotificationAsync(path, ct);
                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug("Created notification for {Path}: FileSize={FileSize}, HasContent={HasContent}", path, notification.FileSize, !string.IsNullOrEmpty(notification.Content));
                }
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
                if (_logger.IsEnabled(LogLevel.Warning))
                {
                    _logger.LogWarning(ex, "Error processing file {Path}", path);
                }
                _diagnostics.RecordFileEvent(path, false, null);
            }
        }
    }

    private async Task WaitForFileReadyAsync(string path, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < _currentConfig.WaitForFileReadyMilliseconds)
        {
            try
            {
                using var fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                break; // File is ready
            }
            catch
            {
                await Task.Delay(50, ct);
            }
        }
    }

    private async Task ProcessFileAsync(string path, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_currentConfig.ApiEndpoint))
        {
            if (_logger.IsEnabled(LogLevel.Warning))
            {
                _logger.LogWarning("ApiEndpoint not configured. Skipping file {Path}", path);
            }
            return;
        }

        try
        {
            if (_logger.IsEnabled(LogLevel.Trace))
            {
                _logger.LogTrace("Processing file {Path}, PostFileContents: {PostContents}, MoveAfterProcessing: {MoveFiles}",
                    path, _currentConfig.PostFileContents, _currentConfig.MoveProcessedFiles);
            }

            var notification = await CreateNotificationAsync(path, ct);
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("Created notification for {Path}: FileSize={FileSize}, HasContent={HasContent}", path, notification.FileSize, !string.IsNullOrEmpty(notification.Content));
            }
            var success = await SendNotificationAsync(notification, ct);

            if (success && _currentConfig.MoveProcessedFiles)
            {
                await MoveToProcessedFolderAsync(path, ct);
            }
        }
        catch (Exception ex)
        {
            if (_logger.IsEnabled(LogLevel.Error))
            {
                _logger.LogError(ex, "Failed to process file {Path}", path);
            }
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
                    if (_logger.IsEnabled(LogLevel.Warning))
                    {
                        _logger.LogWarning("File {Path} exceeds MaxContentBytes ({Limit} bytes); sending metadata only.", path, _currentConfig.MaxContentBytes);
                    }
                    notification.Content = null; // Do not include contents that exceed the configured limit
                }
            }
            catch (Exception ex)
            {
                if (_logger.IsEnabled(LogLevel.Warning))
                {
                    _logger.LogWarning(ex, "Failed to read file contents for {Path}", path);
                }
                // Continue without contents
            }
        }

        return notification;
    }

    internal async Task<bool> SendNotificationAsync(FileNotification notification, CancellationToken ct)
    {
        // Check circuit-breaker status early (per-endpoint)
        var endpointKey = string.IsNullOrWhiteSpace(_currentConfig.ApiEndpoint) ? string.Empty : _currentConfig.ApiEndpoint;
        if (_currentConfig.EnableCircuitBreaker)
        {
            var state = _circuitStates.GetOrAdd(endpointKey, _ => new CircuitState());
            lock (state.Lock)
            {
                if (state.OpenUntil.HasValue && state.OpenUntil.Value > DateTimeOffset.Now)
                {
                    if (_logger.IsEnabled(LogLevel.Warning))
                    {
                        _logger.LogWarning("Circuit breaker is open for endpoint {Endpoint}; skipping post for {Path}", endpointKey, notification.Path);
                    }
                    _diagnostics.RecordFileEvent(notification.Path, false, null);
                    return false;
                }
            }
        }
        // Use the typed HttpClient
        using var fileClient = _httpClientFactory.CreateClient("fileApi");
        HttpResponseMessage? response = null;
        Exception? lastException = null;

        // Implement a simple retry loop (policies removed). The configuration's 'Retries' represents retry count (Polly previously used retries count as retry attempts),
        // so we keep the behavior similar: perform (Retries + 1) total attempts.
        var retryCount = Math.Max(0, _currentConfig.Retries);
        var attempts = retryCount + 1;
        var baseDelayMs = Math.Max(100, _currentConfig.RetryDelayMilliseconds);

        var totalSw = System.Diagnostics.Stopwatch.StartNew();
        for (int attempt = 1; attempt <= attempts; attempt++)
        {
            System.Diagnostics.Stopwatch? attemptSw = null;
            long attemptLatency = 0;
            try
            {
                if (_logger.IsEnabled(LogLevel.Trace))
                {
                    _logger.LogTrace("Posting {Path} to {Endpoint} (attempt {Attempt}/{Attempts})", notification.Path, _currentConfig.ApiEndpoint, attempt, attempts);
                }
                attemptSw = System.Diagnostics.Stopwatch.StartNew();

                // Decide sending strategy: small files -> JSON payload including content; larger but acceptable files -> multipart stream
                var streamThreshold = Math.Max(0, _currentConfig.StreamingThresholdBytes);
                if (_currentConfig.PostFileContents && notification.FileSize.HasValue && notification.FileSize.Value > streamThreshold && notification.FileSize.Value <= _currentConfig.MaxContentBytes)
                {
                    // Stream the file content using multipart/form-data: metadata (JSON) + file stream
                    try
                    {
                        var metadataObj = new UploadMetadata
                        {
                            Path = notification.Path,
                            FileSize = notification.FileSize,
                            LastWriteTime = notification.LastWriteTime
                        };

                        using var multipart = new MultipartFormDataContent();
                        var metadataJson = JsonSerializer.Serialize(metadataObj, MyJsonContext.Default.UploadMetadata);
                        var metadataContent = new StringContent(metadataJson, Encoding.UTF8);
                        metadataContent.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                        multipart.Add(metadataContent, "metadata");

                        // Open file stream for streaming upload
                        using var fs = File.Open(notification.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
                        var streamContent = new StreamContent(fs);
                        streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                        multipart.Add(streamContent, "file", Path.GetFileName(notification.Path));

                        // Create request message so we can set Authorization per-request safely
                        using var req = new HttpRequestMessage(HttpMethod.Post, _currentConfig.ApiEndpoint) { Content = multipart };
                        if (!string.IsNullOrWhiteSpace(_currentConfig.BearerToken))
                        {
                            var token = _currentConfig.BearerToken;
                            if (token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) token = token[7..].Trim();
                            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                            // Log a masked preview for debugging without exposing the full token
                            if (_logger.IsEnabled(LogLevel.Debug))
                            {
                                var preview = token.Length > 4 ? $"****{token[^4..]}" : "****";
                                _logger.LogDebug("Attaching Authorization header for endpoint {Endpoint}. Token preview: {TokenPreview}", _currentConfig.ApiEndpoint, preview);
                            }
                        }

                        response = await fileClient.SendAsync(req, ct);
                    }
                    catch (Exception ex)
                    {
                        // If streaming fails, fall back to metadata-only post and continue
                        lastException = ex;
                        if (_logger.IsEnabled(LogLevel.Debug))
                        {
                            _logger.LogDebug(ex, "Streaming upload failed for {Path}; falling back to metadata-only (attempt {Attempt}/{Attempts})", notification.Path, attempt, attempts);
                        }
                        _diagnostics.RecordFileEvent(notification.Path, false, null);
                        if (attempt >= attempts)
                        {
                            // final attempt failure will be handled below
                            break;
                        }
                        // otherwise allow another attempt
                    }
                }
                else
                {
                    using var content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(notification, MyJsonContext.Default.FileNotification));
                    content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

                    // Build request to include Authorization header per-request
                    using var req = new HttpRequestMessage(HttpMethod.Post, _currentConfig.ApiEndpoint) { Content = content };
                    if (!string.IsNullOrWhiteSpace(_currentConfig.BearerToken))
                    {
                        var token = _currentConfig.BearerToken;
                        if (token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) token = token[7..].Trim();
                        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                        if (_logger.IsEnabled(LogLevel.Debug))
                        {
                            var preview = token.Length > 4 ? $"****{token[^4..]}" : "****";
                            _logger.LogDebug("Attaching Authorization header for endpoint {Endpoint}. Token preview: {TokenPreview}", _currentConfig.ApiEndpoint, preview);
                        }
                    }

                    response = await fileClient.SendAsync(req, ct);
                }

                attemptSw?.Stop();
                attemptLatency = attemptSw?.ElapsedMilliseconds ?? 0;
                if (response is not null && response.IsSuccessStatusCode)
                {
                    if (_logger.IsEnabled(LogLevel.Information))
                    {
                        _logger.LogInformation("Successfully posted file {Path} with StatusCode {StatusCode} in {AttemptLatencyMs}ms (attempt {Attempt}/{Attempts})",
                            notification.Path, (int)response.StatusCode, attemptLatency, attempt, attempts);
                        if (_logger.IsEnabled(LogLevel.Debug))
                        {
                            _logger.LogDebug("SendDetails: {Endpoint} {Attempt} {Attempts} {AttemptLatencyMs} {FileSize}", _currentConfig.ApiEndpoint, attempt, attempts, attemptLatency, notification.FileSize);
                        }
                    }

                    // Success -> reset circuit breaker for this endpoint
                    if (_currentConfig.EnableCircuitBreaker)
                    {
                        var state = _circuitStates.GetOrAdd(endpointKey, _ => new CircuitState());
                        lock (state.Lock)
                        {
                            state.Failures = 0;
                            state.OpenUntil = null;
                        }
                        _diagnostics.UpdateCircuitState(endpointKey, 0, null);
                    }
                    _diagnostics.RecordFileEvent(notification.Path, true, (int)response.StatusCode);
                    return true;
                }

                // On retryable server error, attemptSw may still be running; stop timing and record latency
                attemptSw?.Stop();
                var attemptLatencyLocal = attemptLatency;
                if (response is not null && (int)response.StatusCode >= 500 && attempt < attempts)
                {
                    // Server error and we'll retry; surface as Warning because it's likely actionable
                    if (_logger.IsEnabled(LogLevel.Warning))
                    {
                        _logger.LogWarning("Transient API response {StatusCode} for {Path} (attempt {Attempt}/{Attempts}); will retry - latency={AttemptLatencyMs}ms",
                            response.StatusCode, notification.Path, attempt, attempts, attemptLatencyLocal);
                    }
                    // fall through to delay & retry
                }
                else
                {
                    if (_logger.IsEnabled(LogLevel.Warning))
                    {
                        _logger.LogWarning("API returned {StatusCode} for {Path} (attempt {Attempt}/{Attempts}; latency={AttemptLatencyMs}ms)", response?.StatusCode, notification.Path, attempt, attempts, attemptLatencyLocal);
                    }
                    // Final non-success response -> treat as failure for circuit breaker
                    if (_currentConfig.EnableCircuitBreaker)
                    {
                        var state = _circuitStates.GetOrAdd(endpointKey, _ => new CircuitState());
                        lock (state.Lock)
                        {
                            state.Failures++;
                            if (state.Failures >= _currentConfig.CircuitBreakerFailureThreshold)
                            {
                                state.OpenUntil = DateTimeOffset.Now.AddMilliseconds(_currentConfig.CircuitBreakerOpenDurationMilliseconds);
                                if (_logger.IsEnabled(LogLevel.Error))
                                {
                                    _logger.LogError("Circuit breaker opened for endpoint {Endpoint} due to {Failures} consecutive failures", endpointKey, state.Failures);
                                }
                                _diagnostics.UpdateCircuitState(endpointKey, state.Failures, state.OpenUntil);
                            }
                        }
                    }
                    _diagnostics.RecordFileEvent(notification.Path, false, response is not null ? (int)response.StatusCode : null);
                    return false;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return false;
            }
            catch (Exception ex)
            {
                lastException = ex;
                _diagnostics.RecordFileEvent(notification.Path, false, null);
                if (attempt < attempts)
                {
                    attemptSw?.Stop();
                    var attemptLatencyEx = attemptLatency;
                    if (_logger.IsEnabled(LogLevel.Debug))
                    {
                        _logger.LogDebug(ex, "Exception posting file {Path} on attempt {Attempt}/{Attempts}; will retry (latency={AttemptLatencyMs}ms)", notification.Path, attempt, attempts, attemptLatencyEx);
                    }
                }
                else
                {
                    // Last attempt failed
                    if (_logger.IsEnabled(LogLevel.Warning))
                    {
                        var lastAttemptLatency = attemptLatency;
                        _logger.LogWarning(ex, "All attempts failed posting file {Path} after {Attempts} attempts (lastAttemptLatency={AttemptLatencyMs}ms)", notification.Path, attempts, lastAttemptLatency);
                    }
                }
                // If this was the last attempt, break and fail
                if (attempt >= attempts)
                {
                    // Register failure towards circuit breaker
                    if (_currentConfig.EnableCircuitBreaker)
                    {
                        var state = _circuitStates.GetOrAdd(endpointKey, _ => new CircuitState());
                        lock (state.Lock)
                        {
                            state.Failures++;
                            if (state.Failures >= _currentConfig.CircuitBreakerFailureThreshold)
                            {
                                state.OpenUntil = DateTimeOffset.Now.AddMilliseconds(_currentConfig.CircuitBreakerOpenDurationMilliseconds);
                                if (_logger.IsEnabled(LogLevel.Error))
                                {
                                    _logger.LogError("Circuit breaker opened due to {Failures} consecutive exceptions for endpoint {Endpoint}", state.Failures, endpointKey);
                                }
                            }
                        }
                        _diagnostics.UpdateCircuitState(endpointKey, state.Failures, state.OpenUntil);
                    }
                    break;
                }
            }

            // Exponential backoff with jitter before the next attempt
            if (attempt < attempts)
            {
                var jitter = Random.Shared.Next(0, 100);
                var delay = (int)(baseDelayMs * Math.Pow(2, attempt - 1)) + jitter;
                try
                {
                    await Task.Delay(delay, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return false;
                }
            }
        }

        // All attempts failed - ensure the last exception is logged if present, include total elapsed
        totalSw.Stop();
        var totalLatency = totalSw.ElapsedMilliseconds;
        if (lastException is not null)
        {
            if (_logger.IsEnabled(LogLevel.Error))
            {
                _logger.LogError(lastException, "Failed to post file {Path} after {Attempts} attempts in {TotalLatencyMs}ms", notification.Path, attempts, totalLatency);
            }
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
            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("Moved processed file {From} to {To}", filePath, processedPath);
            }
        }
        catch (Exception ex)
        {
            if (_logger.IsEnabled(LogLevel.Warning))
            {
                _logger.LogWarning(ex, "Failed to move processed file {Path}", filePath);
            }
        }

        return Task.CompletedTask;
    }

    private async Task OnConfigurationChanged(ExternalConfiguration newConfig)
    {
        if (!await _configReloadLock.WaitAsync(0))
            return; // Already reloading

        try
        {
            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("Configuration changed, reloading watchers");
            }

            // Stop current watchers
            await StopAllWatchersAsync();

            // Start new watchers
            if (newConfig.Folders.Length > 0)
            {
                await StartWatchingFoldersAsync(newConfig.Folders);
            }

            // Apply logging configuration from the reloaded config
            ConfigureLogging(newConfig.Logging?.LogLevel);

            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("Configuration reload completed");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reload configuration");
        }
        finally
        {
            _configReloadLock.Release();
        }
    }

    private Task StopAllWatchersAsync()
    {
        List<FileSystemWatcher> watchersToStop;

        lock (_watchersLock)
        {
            watchersToStop = _watchers.ToList();
            _watchers.Clear();
        }

        foreach (var watcher in watchersToStop)
        {
            try
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
                _diagnostics.UnregisterWatcher(watcher.Path);
            }
            catch (Exception ex)
            {
                if (_logger.IsEnabled(LogLevel.Warning))
                {
                    _logger.LogWarning(ex, "Error stopping watcher for {Path}", watcher.Path);
                }
            }
        }

        return Task.CompletedTask;
    }

    private void OnWatcherError(string folder, ErrorEventArgs e)
    {
        if (_logger.IsEnabled(LogLevel.Warning))
        {
            _logger.LogWarning(e.GetException(), "FileSystemWatcher error for folder {Folder}", folder);
        }

        var attempts = _watcherRestartAttempts.AddOrUpdate(folder, 1, (_, cur) => cur + 1);

        if (attempts <= _currentConfig.WatcherMaxRestartAttempts)
        {
            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("Attempting to restart watcher for {Folder} (attempt {Attempt}/{Max})",
                    folder, attempts, _currentConfig.WatcherMaxRestartAttempts);
            }

            _ = Task.Run(async () =>
            {
                await Task.Delay(_currentConfig.WatcherRestartDelayMilliseconds);
                await RestartWatcherForFolderAsync(folder);
            });
        }
        else
        {
            if (_logger.IsEnabled(LogLevel.Error))
            {
                _logger.LogError("Watcher for {Folder} failed after {Max} attempts, stopping service",
                    folder, _currentConfig.WatcherMaxRestartAttempts);
            }

            _ = Task.Run(async () =>
            {
                await Task.Delay(500);
                _lifetime.StopApplication();
            });
        }
    }

    private async Task RestartWatcherForFolderAsync(string folder)
    {
        try
        {
            // Remove failed watchers for this folder
            List<FileSystemWatcher> toRemove;
            lock (_watchersLock)
            {
                toRemove = _watchers.Where(w => string.Equals(w.Path, folder, StringComparison.OrdinalIgnoreCase)).ToList();
                foreach (var w in toRemove)
                {
                    _watchers.Remove(w);
                }
            }

            foreach (var watcher in toRemove)
            {
                try
                {
                    watcher.EnableRaisingEvents = false;
                    watcher.Dispose();
                }
                catch (Exception ex)
                {
                    if (_logger.IsEnabled(LogLevel.Warning))
                    {
                        _logger.LogWarning(ex, "Error disposing old watcher for {Folder}", folder);
                    }
                }
            }

            _diagnostics.UnregisterWatcher(folder);

            // Create new watcher
            await StartWatchingFoldersAsync(new[] { folder });

            _watcherRestartAttempts.TryRemove(folder, out _);
            _diagnostics.ResetRestart(folder);

            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("Successfully restarted watcher for {Folder}", folder);
            }
        }
        catch (Exception ex)
        {
            if (_logger.IsEnabled(LogLevel.Warning))
            {
                _logger.LogWarning(ex, "Failed to restart watcher for {Folder}", folder);
            }
        }
    }

    private async Task CleanupAsync()
    {
        await StopAllWatchersAsync();

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

    private void ConfigureLogging(string? logLevelString)
    {
        // If null, treat as Information
        logLevelString ??= "Information";
        try
        {
            if (Enum.TryParse<LogLevel>(logLevelString, true, out var configuredLevel))
            {
                // Note: Dynamic log level configuration in .NET requires special setup
                // For now, we'll log the configuration and recommend restart for level changes
                if (_logger.IsEnabled(LogLevel.Information))
                {
                    _logger.LogInformation("Logging level configured to {LogLevel}. Note: Restart service for log level changes to take full effect", configuredLevel);
                }

                // Store the configured level for conditional logging checks
                _configuredLogLevel = configuredLevel;
            }
            else
            {
                if (_logger.IsEnabled(LogLevel.Warning))
                {
                    _logger.LogWarning("Invalid LogLevel '{LogLevel}' in configuration. Valid values: Trace, Debug, Information, Warning, Error, Critical, None", logLevelString);
                }
                _configuredLogLevel = LogLevel.Information;
            }
        }
        catch (Exception ex)
        {
            if (_logger.IsEnabled(LogLevel.Warning))
            {
                _logger.LogWarning(ex, "Failed to configure logging level from '{LogLevel}', using Information", logLevelString);
            }
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
                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug("No folders configured - skipping existing-files scan");
                }
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
                        if (_logger.IsEnabled(LogLevel.Warning))
                        {
                            _logger.LogWarning("Configured folder does not exist: {Folder}", folder);
                        }
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

                        // Mark pending and enqueue respecting debounce/channel settings
                        _pending[filePath] = DateTime.Now;

                        if (_currentConfig.DebounceMilliseconds <= 0 && _sendChannel is not null)
                        {
                            if (!_sendChannel.Writer.TryWrite(filePath))
                            {
                                if (_logger.IsEnabled(LogLevel.Warning))
                                {
                                    _logger.LogWarning("Failed to queue existing file {Path} - channel full", filePath);
                                }
                            }
                        }
                        _diagnostics.IncrementEnqueued();

                        enqueued++;

                        // Avoid overly long startup scans; log progress periodically
                        if ((enqueued % 500) == 0)
                        {
                            if (_logger.IsEnabled(LogLevel.Information))
                            {
                                _logger.LogInformation("Enqueued {Count} existing files from {Folder}", enqueued, folder);
                            }
                        }
                    }

                    if (_logger.IsEnabled(LogLevel.Information))
                    {
                        _logger.LogInformation("Completed enqueueing {Count} existing files from {Folder}", enqueued, folder);
                    }
                }
                catch (Exception ex)
                {
                    if (_logger.IsEnabled(LogLevel.Warning))
                    {
                        _logger.LogWarning(ex, "Failed scanning folder {Folder} for existing files", folder);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            if (_logger.IsEnabled(LogLevel.Warning))
            {
                _logger.LogWarning(ex, "Unexpected error scanning folders for existing files");
            }
        }

        return Task.CompletedTask;
    }
}
