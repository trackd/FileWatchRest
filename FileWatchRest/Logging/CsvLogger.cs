using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace FileWatchRest.Logging;

public sealed class CsvLoggerProvider : ILoggerProvider
{
    private readonly string _logFilePath;
    private readonly object _lock = new();
    private StreamWriter? _writer;
    private bool _disposed;

    public CsvLoggerProvider(string serviceName)
    {
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var logDir = Path.Combine(programData, serviceName, "logs");
        Directory.CreateDirectory(logDir);
        var fileName = $"{serviceName}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv";
        _logFilePath = Path.Combine(logDir, fileName);
        // ensure writer lazily opened
    }

    private void EnsureWriter()
    {
        if (_writer != null) return;
        lock (_lock)
        {
            if (_writer != null) return;
            _writer = new StreamWriter(new FileStream(_logFilePath, FileMode.Append, FileAccess.Write, FileShare.Read)) { AutoFlush = true };
            // write header
            _writer.WriteLine("Timestamp,Level,Category,Message,Exception");
        }
    }

    public ILogger CreateLogger(string categoryName) => new CsvLogger(this, categoryName);

    internal void Write(string category, LogLevel level, string message, Exception? ex)
    {
        try
        {
            EnsureWriter();
            var ts = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            var lvl = level.ToString();
            var exc = ex?.ToString() ?? string.Empty;
            var line = ToCsv(ts, lvl, category, message, exc);
            lock (_lock)
            {
                _writer!.WriteLine(line);
            }
        }
        catch
        {
            // suppress logging errors
        }
    }

    private static string ToCsv(params string[] fields)
    {
        // Simple CSV: quote fields that contain comma/quote/newline and escape quotes
        var sb = new StringBuilder();
        for (int i = 0; i < fields.Length; i++)
        {
            var f = fields[i] ?? string.Empty;
            bool needsQuote = f.Contains(',') || f.Contains('"') || f.Contains('\n') || f.Contains('\r');
            if (needsQuote)
            {
                var escaped = f.Replace("\"", "\"\"");
                sb.Append('"').Append(escaped).Append('"');
            }
            else
            {
                sb.Append(f);
            }
            if (i + 1 < fields.Length) sb.Append(',');
        }
        return sb.ToString();
    }

    public void Dispose()
    {
        if (_disposed) return;
        lock (_lock)
        {
            _writer?.Dispose();
            _writer = null;
            _disposed = true;
        }
    }
}

internal sealed class CsvLogger : ILogger
{
    private readonly CsvLoggerProvider _provider;
    private readonly string _category;

    public CsvLogger(CsvLoggerProvider provider, string category)
    {
        _provider = provider;
        _category = category;
    }

    IDisposable ILogger.BeginScope<TState>(TState state) => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;
        var message = formatter(state, exception);
        _provider.Write(_category, logLevel, message, exception);
    }

    private class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new NullScope();
        public void Dispose() { }
    }
}
