namespace FileWatchRest.Services;

/// <summary>
/// Background service that manages file event debouncing.
/// Collects file change events and delays processing until the debounce period expires.
/// </summary>
/// <param name="logger"></param>
/// <param name="outputWriter"></param>
/// <param name="getConfig"></param>
public class FileDebounceService(
    ILogger<FileDebounceService> logger,
    ChannelWriter<string> outputWriter,
    Func<ExternalConfiguration> getConfig) : BackgroundService {
    private readonly ILogger<FileDebounceService> _logger = logger;
    private readonly ConcurrentDictionary<string, DateTime> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly ChannelWriter<string> _outputWriter = outputWriter;
    private readonly Func<ExternalConfiguration> _getConfig = getConfig;

    /// <summary>
    /// Schedule a file path for debounced processing.
    /// Made virtual to enable testing scenarios to intercept scheduling.
    /// </summary>
    /// <param name="path"></param>
    public virtual void Schedule(string path) => _pending.AddOrUpdate(path, DateTime.UtcNow, (_, __) => DateTime.UtcNow);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        LoggerDelegates.FileDebounceStarted(_logger, null);

        try {
            while (!stoppingToken.IsCancellationRequested) {
                try {
                    if (_pending.IsEmpty) {
                        await Task.Delay(100, stoppingToken);
                        continue;
                    }

                    ExternalConfiguration config = _getConfig();
                    DateTime now = DateTime.UtcNow;

                    // Collect files that have exceeded their debounce period
                    var due = _pending
                        .Where(kv => (now - kv.Value).TotalMilliseconds >= config.DebounceMilliseconds)
                        .Select(kv => kv.Key)
                        .ToList();

                    // Remove from pending and write to output channel
                    var processed = new List<string>();
                    foreach (string? key in due) {
                        if (_pending.TryRemove(key, out _)) {
                            processed.Add(key);
                        }
                    }

                    // Write processed files to channel
                    if (processed.Count > 0) {
                        foreach (string path in processed) {
                            // Try immediate write with timeout fallback
                            if (!_outputWriter.TryWrite(path)) {
                                Task writeTask = _outputWriter.WriteAsync(path, stoppingToken).AsTask();
                                Task completed = await Task.WhenAny(writeTask, Task.Delay(1000, stoppingToken));

                                if (completed != writeTask) {
                                    LoggerDelegates.FileDebounceWriteTimeout(_logger, path, null);
                                }
                            }
                        }
                    }

                    await Task.Delay(100, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                    break;
                }
                catch (Exception ex) {
                    LoggerDelegates.FileDebounceError(_logger, ex);
                    await Task.Delay(1000, stoppingToken);
                }
            }
        }
        finally {
            LoggerDelegates.FileDebounceStopped(_logger, null);
        }
    }

    public override void Dispose() {
        _pending.Clear();
        try { base.Dispose(); } catch { }
        GC.SuppressFinalize(this);
    }
}
