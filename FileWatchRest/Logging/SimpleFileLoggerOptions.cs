namespace FileWatchRest.Logging;

public sealed class SimpleFileLoggerOptions
{
    // Unified log type: Csv, Json, or Both
    public LogType LogType { get; set; } = LogType.Csv;

    // Single file name/pattern; provider will append the appropriate extension when needed.
    // Use formatting placeholder {0:...} for per-run timestamping.
    public string FilePathPattern { get; set; } = "logs/FileWatchRest_{0:yyyyMMdd_HHmmss}";

    public int RetainedFileCountLimit { get; set; } = 14;
    public LogLevel LogLevel { get; set; } = LogLevel.Information;
}
