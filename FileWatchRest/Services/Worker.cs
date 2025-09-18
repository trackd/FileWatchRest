using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Options;
using FileWatchRest.Configuration;
using FileWatchRest.Models;

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
    private ExternalConfiguration _currentConfig = new();
    private LogLevel _configuredLogLevel = LogLevel.Information;

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
        ConfigureLogging(_currentConfig.LogLevel);

        _logger.LogInformation("Loaded external configuration with {FolderCount} folders", _currentConfig.Folders.Length);

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
                var now = DateTime.UtcNow;
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
                _logger.LogInformation("Watching folder: {Folder}", folder);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to watch folder {Folder}", folder);
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
            _logger.LogDebug("Ignoring file in processed folder: {Path}", e.FullPath);
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

        _pending[e.FullPath] = DateTime.UtcNow;

        if (_logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("Queued file: {Path}", e.FullPath);

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

                await ProcessFileAsync(path, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error processing file {Path}", path);
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
            _logger.LogWarning("ApiEndpoint not configured. Skipping file {Path}", path);
            return;
        }

        try
        {
            if (_logger.IsEnabled(LogLevel.Trace))
                _logger.LogTrace("Processing file {Path}, PostFileContents: {PostContents}, MoveAfterProcessing: {MoveFiles}",
                    path, _currentConfig.PostFileContents, _currentConfig.MoveProcessedFiles);

            var notification = await CreateNotificationAsync(path, ct);
            var success = await SendNotificationAsync(notification, ct);

            if (success && _currentConfig.MoveProcessedFiles)
            {
                await MoveToProcessedFolderAsync(path, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process file {Path}", path);
            _diagnostics.RecordFileEvent(path, false, null);
        }
    }

    private async Task<FileNotification> CreateNotificationAsync(string path, CancellationToken ct)
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
                notification.Content = await File.ReadAllTextAsync(path, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read file contents for {Path}", path);
                // Continue without contents
            }
        }

        return notification;
    }

    private async Task<bool> SendNotificationAsync(FileNotification notification, CancellationToken ct)
    {
        using var client = _httpClientFactory.CreateClient();

        // Add bearer token if configured
        if (!string.IsNullOrWhiteSpace(_currentConfig.BearerToken))
        {
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _currentConfig.BearerToken);
        }

        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(notification, MyJsonContext.Default.FileNotification);

        for (int attempt = 1; attempt <= Math.Max(1, _currentConfig.Retries); attempt++)
        {
            try
            {
                using var content = new ByteArrayContent(jsonBytes);
                content.Headers.ContentType = new("application/json");

                var response = await client.PostAsync(_currentConfig.ApiEndpoint, content, ct);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Successfully posted file {Path}", notification.Path);

                    if (_logger.IsEnabled(LogLevel.Debug))
                        _logger.LogDebug("API response: {StatusCode} for file {Path}, size: {Size} bytes",
                            response.StatusCode, notification.Path, jsonBytes.Length);

                    _diagnostics.RecordFileEvent(notification.Path, true, (int)response.StatusCode);
                    return true;
                }
                else
                {
                    _logger.LogWarning("API returned {StatusCode} for {Path}", response.StatusCode, notification.Path);
                    _diagnostics.RecordFileEvent(notification.Path, false, (int)response.StatusCode);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to post file (attempt {Attempt}) for {Path}", attempt, notification.Path);
            }

            if (attempt < _currentConfig.Retries)
            {
                await Task.Delay(_currentConfig.RetryDelayMilliseconds, ct);
            }
        }

        _logger.LogError("Failed to post file {Path} after {Retries} attempts", notification.Path, _currentConfig.Retries);
        _diagnostics.RecordFileEvent(notification.Path, false, null);
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
            _logger.LogInformation("Moved processed file {From} to {To}", filePath, processedPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to move processed file {Path}", filePath);
        }

        return Task.CompletedTask;
    }

    private async void OnConfigurationChanged(ExternalConfiguration newConfig)
    {
        if (!await _configReloadLock.WaitAsync(0))
            return; // Already reloading

        try
        {
            _logger.LogInformation("Configuration changed, reloading watchers");

            // Stop current watchers
            await StopAllWatchersAsync();

            // Update configuration
            _currentConfig = newConfig;

            // Apply logging configuration
            ConfigureLogging(newConfig.LogLevel);

            // Start new watchers
            if (newConfig.Folders.Length > 0)
            {
                await StartWatchingFoldersAsync(newConfig.Folders);
            }

            _logger.LogInformation("Configuration reload completed");
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
            watchersToStop = [.. _watchers];
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
                _logger.LogWarning(ex, "Error stopping watcher for {Path}", watcher.Path);
            }
        }

        return Task.CompletedTask;
    }

    private void OnWatcherError(string folder, ErrorEventArgs e)
    {
        _logger.LogWarning(e.GetException(), "FileSystemWatcher error for folder {Folder}", folder);

        var attempts = _watcherRestartAttempts.AddOrUpdate(folder, 1, (_, cur) => cur + 1);

        if (attempts <= _currentConfig.WatcherMaxRestartAttempts)
        {
            _logger.LogInformation("Attempting to restart watcher for {Folder} (attempt {Attempt}/{Max})",
                folder, attempts, _currentConfig.WatcherMaxRestartAttempts);

            _ = Task.Run(async () =>
            {
                await Task.Delay(_currentConfig.WatcherRestartDelayMilliseconds);
                await RestartWatcherForFolderAsync(folder);
            });
        }
        else
        {
            _logger.LogError("Watcher for {Folder} failed after {Max} attempts, stopping service",
                folder, _currentConfig.WatcherMaxRestartAttempts);

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
                    _logger.LogWarning(ex, "Error disposing old watcher for {Folder}", folder);
                }
            }

            _diagnostics.UnregisterWatcher(folder);

            // Create new watcher
            await StartWatchingFoldersAsync([folder]);

            _watcherRestartAttempts.TryRemove(folder, out _);
            _diagnostics.ResetRestart(folder);

            _logger.LogInformation("Successfully restarted watcher for {Folder}", folder);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to restart watcher for {Folder}", folder);
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

    private void ConfigureLogging(string logLevelString)
    {
        try
        {
            if (Enum.TryParse<LogLevel>(logLevelString, true, out var configuredLevel))
            {
                // Note: Dynamic log level configuration in .NET requires special setup
                // For now, we'll log the configuration and recommend restart for level changes
                _logger.LogInformation("Logging level configured to {LogLevel}. Note: Restart service for log level changes to take full effect", configuredLevel);

                // Store the configured level for conditional logging checks
                _configuredLogLevel = configuredLevel;
            }
            else
            {
                _logger.LogWarning("Invalid LogLevel '{LogLevel}' in configuration. Valid values: Trace, Debug, Information, Warning, Error, Critical, None", logLevelString);
                _configuredLogLevel = LogLevel.Information;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to configure logging level from '{LogLevel}', using Information", logLevelString);
            _configuredLogLevel = LogLevel.Information;
        }
    }
}
