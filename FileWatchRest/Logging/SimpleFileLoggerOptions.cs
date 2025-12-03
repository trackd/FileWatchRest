namespace FileWatchRest.Logging;

public sealed class SimpleFileLoggerOptions {
    /// <summary>
    /// Unified log type: Csv, Json, or Both
    /// </summary>
    public LogType LogType { get; set; } = LogType.Csv;

    /// <summary>
    /// Single file name/pattern; provider will append the appropriate extension when needed.
    /// Use formatting placeholder {0:...} for per-run timestamping.
    /// </summary>
    public string FilePathPattern { get; set; } = "logs/FileWatchRest_{0:yyyyMMdd_HHmmss}";

    /// <summary>
    /// Number of days to retain log files
    /// </summary>
    public int RetainedDays { get; set; } = 14;

    [JsonConverter(typeof(JsonStringEnumConverter<LogLevel>))]
    public LogLevel? LogLevel { get; set; }
    public SimpleFileLoggerOptions() {
        LogLevel = Microsoft.Extensions.Logging.LogLevel.Information;
    }
}
