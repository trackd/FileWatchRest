namespace FileWatchRest.Services;

/// <summary>
/// Background service that processes debounced file events and sends notifications.
/// Reads from the debounced file channel and coordinates notification dispatch.
/// </summary>
/// <param name="logger"></param>
/// <param name="inputReader"></param>
/// <param name="processFileAsync"></param>
public sealed class FileSenderService(
    ILogger<FileSenderService> logger,
    ChannelReader<string> inputReader,
    Func<string, CancellationToken, ValueTask> processFileAsync) : BackgroundService {
    private readonly ILogger<FileSenderService> _logger = logger;
    private readonly ChannelReader<string> _inputReader = inputReader;
    private readonly Func<string, CancellationToken, ValueTask> _processFileAsync = processFileAsync;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        LoggerDelegates.FileSenderStarted(_logger, null);

        try {
            await foreach (string path in _inputReader.ReadAllAsync(stoppingToken)) {
                try {
                    await _processFileAsync(path, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                    break;
                }
                catch (Exception ex) {
                    LoggerDelegates.FileSenderError(_logger, path, ex);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
            // Expected during shutdown
        }
        finally {
            LoggerDelegates.FileSenderStopped(_logger, null);
        }
    }
}
