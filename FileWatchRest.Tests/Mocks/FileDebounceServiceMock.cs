namespace FileWatchRest.Tests.Mocks;

/// <summary>
/// Mock implementation of FileDebounceService for unit testing.
/// Provides synchronous scheduling without actual background processing.
/// </summary>
public sealed class FileDebounceServiceMock(
    ILogger<FileDebounceService>? logger = null,
    ChannelWriter<string>? writer = null,
    Func<ExternalConfiguration>? getConfig = null) : FileDebounceService(
        logger ?? NullLogger<FileDebounceService>.Instance,
        writer ?? Channel.CreateUnbounded<string>().Writer,
        getConfig ?? (() => new ExternalConfiguration())) {
    private readonly List<string> _scheduledPaths = [];

    public IReadOnlyList<string> ScheduledPaths => _scheduledPaths.AsReadOnly();

    public new void Schedule(string path) {
        _scheduledPaths.Add(path);
        base.Schedule(path);
    }

    public void ClearScheduled() => _scheduledPaths.Clear();

    // Override ExecuteAsync to prevent actual background processing in tests
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.CompletedTask;
}
