namespace FileWatchRest.Tests.TestUtilities;

public class TestLogger<T> : ILogger<T> {
    public record LogEntry(LogLevel Level, EventId EventId, string Message, Exception? Exception);
    private readonly List<LogEntry> _entries = [];
    public IReadOnlyList<LogEntry> Entries => _entries;

    IDisposable ILogger.BeginScope<TState>(TState state) => NullScope.Instance;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) {
        string msg = formatter(state, exception);
        _entries.Add(new LogEntry(logLevel, eventId, msg, exception));
    }

    private sealed class NullScope : IDisposable {
        public static NullScope Instance { get; } = new NullScope();
        public void Dispose() { }
    }
}
