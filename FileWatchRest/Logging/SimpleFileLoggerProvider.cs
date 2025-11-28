

/// <summary>
/// Provides file-based logging with support for daily rotation, retention, and multiple log formats.
/// </summary>
namespace FileWatchRest.Logging;

/// <summary>
/// File logger provider supporting CSV and JSON formats, daily rotation, and retention by days.
/// </summary>
public sealed partial class SimpleFileLoggerProvider : ILoggerProvider {
    private readonly Lock _sync = new();
    private readonly ConcurrentDictionary<string, SimpleFileLogger> _loggers = new();
    private StreamWriter? _jsonWriter;
    private StreamWriter? _csvWriter;

    private DateTime _currentLogDate = DateTime.MinValue;
    private string _currentLogFile = string.Empty;

    [GeneratedRegex("(\\d{4})(?:-?)(\\d{1,2})(?:-?)(\\d{1,2})")]
    private static partial Regex DatePattern();

    public SimpleFileLoggerOptions Options { get; }

    /// <summary>
    /// Initializes a new instance of the SimpleFileLoggerProvider with the specified options.
    /// </summary>
    /// <param name="options">Logger options.</param>
    public SimpleFileLoggerProvider(SimpleFileLoggerOptions options) {
        Options = options;
        EnsureWriters();
    }

    /// <summary>
    /// Creates or retrieves a logger for the specified category.
    /// </summary>
    /// <param name="categoryName">The logger category name.</param>
    /// <returns>A logger instance.</returns>
    public ILogger CreateLogger(string categoryName) => _loggers.GetOrAdd(categoryName, name => new SimpleFileLogger(name, this));

    /// <summary>
    /// Ensures log writers are initialized for the current run and log type.
    /// </summary>
    private void EnsureWriters() {
        try {
            // Resolve a base pattern from the configured FilePathPattern. If the pattern is relative, store logs under the common AppData service directory.
            string serviceDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "FileWatchRest");
            string pattern = Options.FilePathPattern ?? "logs/FileWatchRest_{0:yyyyMMdd_HHmms}";
            if (!Path.IsPathRooted(pattern)) {
                pattern = Path.Combine(serviceDir, pattern);
            }
            // If pattern contains a format placeholder we timestamp it per-run.
            string basePath = pattern.Contains('{') ? string.Format(CultureInfo.InvariantCulture, pattern, DateTime.Now) : pattern;

            static string EnsureFileName(string baseP, string ext) {
                return Path.HasExtension(baseP) ? baseP : baseP + ext;
            }

            // JSON writer
            if (Options.LogType is LogType.Json or LogType.Both) {
                string jsonPath = EnsureFileName(basePath, ".json");
                string dir = Path.GetDirectoryName(jsonPath) ?? ".";
                _ = Directory.CreateDirectory(dir);
                _jsonWriter = File.AppendText(jsonPath);
                _jsonWriter.AutoFlush = true;
            }

            // CSV writer
            if (Options.LogType is LogType.Csv or LogType.Both) {
                string csvPath = EnsureFileName(basePath, ".csv");
                string dir = Path.GetDirectoryName(csvPath) ?? ".";
                Directory.CreateDirectory(dir);
                try {
                    PurgeOldLogFilesByDate(dir, Options.RetainedDays);
                }
                catch { }

                // Minimal expected header (do not force extra columns)
                const string expectedHeader = "Timestamp,Level,Message,Category,Exception,StatusCode";

                // If the file exists but its header does not match the expected minimal header, replace the header using a temp file and replace.
                if (File.Exists(csvPath) && new FileInfo(csvPath).Length > 0) {
                    EnsureCsvHeaderMatches(csvPath, expectedHeader);
                }

                _csvWriter = File.AppendText(csvPath);
                _csvWriter.AutoFlush = true;
                if (_csvWriter.BaseStream.Length == 0) {
                    try { _csvWriter.WriteLine(expectedHeader); } catch (Exception ex) { Console.Error.WriteLine("Failed to write CSV header: " + ex); }
                }
            }
        }
        catch {
            // Don't let logging initialization crash the app
        }
    }

    // Extracted helper to ensure the CSV file contains at least the expected minimal header.
    /// <summary>
    /// Ensures the CSV log file contains the expected header, replacing it if necessary.
    /// Fix: Stream-based approach to avoid loading large files into memory.
    /// </summary>
    /// <param name="csvPath">Path to the CSV log file.</param>
    /// <param name="expectedHeader">Expected header line.</param>
    internal static void EnsureCsvHeaderMatches(string csvPath, string expectedHeader) {
        try {
            // Fix: Use streaming to avoid loading entire file into memory (which can fail for large logs)
            string? firstLine = null;
            using (var fs = new FileStream(csvPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var sr = new StreamReader(fs)) {
                firstLine = sr.ReadLine();
            }

            // If header already matches, nothing to do
            if (string.Equals(firstLine, expectedHeader, StringComparison.Ordinal)) {
                return;
            }

            // Header doesn't match - need to replace it
            string dir = Path.GetDirectoryName(csvPath) ?? ".";
            string tempPath = Path.Combine(dir, $"{Guid.NewGuid():N}.tmp");

            // Stream from source to temp file, replacing only the first line
            using (var sourceStream = new FileStream(csvPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var sourceReader = new StreamReader(sourceStream))
            using (var tempStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var tempWriter = new StreamWriter(tempStream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))) {
                // Write new header
                tempWriter.WriteLine(expectedHeader);

                // Skip old header from source
                _ = sourceReader.ReadLine();

                // Copy remaining lines
                string? line;
                while ((line = sourceReader.ReadLine()) is not null) {
                    tempWriter.WriteLine(line);
                }
            }

            // Replace original with temp file atomically
            try {
                File.Replace(tempPath, csvPath, null);
            }
            catch {
                try { File.Delete(csvPath); } catch { }
                try { File.Move(tempPath, csvPath); } catch { }
                try { if (File.Exists(tempPath)) { File.Delete(tempPath); } } catch { }
            }
        }
        catch {
            // Best-effort: if anything goes wrong we leave the original file untouched.
        }
    }

    /// <summary>
    /// Rotates log files at midnight and purges old files based on retention days.
    /// </summary>
    /// <param name="now">Current timestamp.</param>
    private void EnsureLogFile(DateTime now) {
        DateTime logDate = now.Date;
        string pattern = Options.FilePathPattern ?? "logs/FileWatchRest_{0:yyyyMMdd_HHmms}";
        string serviceDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "FileWatchRest");
        if (!Path.IsPathRooted(pattern)) {
            pattern = Path.Combine(serviceDir, pattern);
        }
        string basePath = pattern.Contains('{') ? string.Format(CultureInfo.InvariantCulture, pattern, logDate) : pattern;
        string dir = Path.GetDirectoryName(basePath) ?? ".";
        if (_currentLogDate != logDate || _currentLogFile != basePath) {
            _currentLogDate = logDate;
            _currentLogFile = basePath;
            PurgeOldLogFilesByDate(dir, Options.RetainedDays);
        }
    }

    /// <summary>
    /// Deletes log files older than the specified retention days.
    /// </summary>
    /// <param name="directory">Directory containing log files.</param>
    /// <param name="retainedDays">Number of days to retain log files.</param>
    internal static void PurgeOldLogFilesByDate(string directory, int retainedDays) {
        // Use UTC-based cutoff to match tests that create files using UTC timestamps.
        DateTime cutoff = DateTime.UtcNow.Date.AddDays(-retainedDays);

        // Use file creation time metadata rather than attempting to parse dates from filenames.
        // This is more robust and avoids fragile regex parsing of naming patterns.
        try {
            Regex datePattern = DatePattern();
            foreach (string file in Directory.GetFiles(directory)) {
                try {
                    var info = new FileInfo(file);
                    DateTime createdUtc = info.CreationTimeUtc.Date;

                    // Prefer file creation time when available
                    if (createdUtc <= cutoff) {
                        try { File.Delete(file); } catch { }
                        continue;
                    }

                    // Fallback: try to parse a date token from the filename (legacy behavior)
                    string name = Path.GetFileNameWithoutExtension(file) ?? string.Empty;
                    Match m = datePattern.Match(name);
                    if (m.Success) {
                        string year = m.Groups[1].Value;
                        string month = m.Groups[2].Value.PadLeft(2, '0');
                        string day = m.Groups[3].Value.PadLeft(2, '0');
                        string token = year + month + day; // yyyyMMdd
                        if (DateTime.TryParseExact(token, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTime fileDate)) {
                            if (fileDate.Date <= cutoff) {
                                try { File.Delete(file); } catch { }
                            }
                        }
                    }
                }
                catch { /* best-effort per-file */ }
            }
        }
        catch { /* best-effort directory scan */ }
    }

    // Extracted core logging implementation to enable easier unit testing of logging behavior
    /// <summary>
    /// Writes a log entry to the configured log files and console.
    /// </summary>
    /// <param name="entry">The log entry to write.</param>
    internal void WriteLine(LogWriteEntry entry) {
        lock (_sync) {
            EnsureLogFile(entry.Timestamp);

            if ((Options.LogType == LogType.Json || Options.LogType == LogType.Both) && _jsonWriter is not null) {
                try {
                    // Use a compact (non-indented) source-generated context for NDJSON log lines
                    string json = JsonSerializer.Serialize(entry, MyJsonLogContext.Default.LogWriteEntry);
                    _jsonWriter.WriteLine(json);
                }
                catch { }
            }

            if ((Options.LogType == LogType.Csv || Options.LogType == LogType.Both) && _csvWriter is not null) {
                try {
                    // Simple CSV escape
                    static string Escape(string s) {
                        return s?.Replace("\"", "\"\"").Replace("\r", " ").Replace("\n", " ") ?? string.Empty;
                    }

                    string line = $"{Escape(entry.Timestamp.ToString("o", CultureInfo.InvariantCulture))},{Escape(entry.Level)},{Escape(entry.Message)},{Escape(entry.FriendlyCategory)},{Escape(entry.Exception)},{(entry.StatusCode.HasValue ? entry.StatusCode.Value.ToString(CultureInfo.InvariantCulture) : string.Empty)}";
                    _csvWriter.WriteLine(line);
                }
                catch { }
            }

            // Also write a concise human-readable console line to make logs easier for operators to scan
            try {
                string consoleMsg = $"{entry.Timestamp:yyyy-MM-dd HH:mm:ss} {entry.Level,-7} {entry.FriendlyCategory}: {entry.Message}";
                if (entry.EventId != 0) {
                    consoleMsg += $" (EventId:{entry.EventId})";
                }
                // Do not include entry.Properties in console output; structured properties are written to JSON file only
                if (!string.IsNullOrEmpty(entry.Exception)) {
                    consoleMsg += $" => {entry.Exception}";
                }

                // Determine color: Information is white by default; treat successful operations (2xx or explicit success messages) as green.
                ConsoleColor previous = Console.ForegroundColor;
                try {
                    ConsoleColor color;

                    // Try to detect an HTTP status code in message/properties
                    int? statusCode = entry.StatusCode ?? TryExtractHttpStatusCode(entry);
                    bool isExplicitSuccessMessage = entry.Message?.IndexOf("Successfully posted", StringComparison.OrdinalIgnoreCase) >= 0
                        || entry.Message?.IndexOf("successfully posted file", StringComparison.OrdinalIgnoreCase) >= 0
                        || entry.Message?.IndexOf("upload succeeded", StringComparison.OrdinalIgnoreCase) >= 0;

                    if (string.Equals(entry.Level, "Information", StringComparison.OrdinalIgnoreCase)) {
                        if (statusCode.HasValue) {
                            color = statusCode.Value is >= 200 and <= 299
                                ? ConsoleColor.Green
                                : statusCode.Value is >= 400 and <= 499
                                    ? ConsoleColor.Yellow
                                    : statusCode.Value is >= 500 and <= 599 ? ConsoleColor.Red : ConsoleColor.White;
                        }
                        else if (isExplicitSuccessMessage) {
                            color = ConsoleColor.Green;
                        }
                        else {
                            color = ConsoleColor.White; // Default info color is white
                        }
                    }
                    else {
                        color = entry.Level switch {
                            "Trace" => ConsoleColor.DarkGray,
                            "Debug" => ConsoleColor.Gray,
                            "Warning" => ConsoleColor.Yellow,
                            "Error" => ConsoleColor.Red,
                            "Critical" => ConsoleColor.Magenta,
                            _ => ConsoleColor.White,
                        };
                    }

                    Console.ForegroundColor = color;
                    Console.WriteLine(consoleMsg);
                }
                finally {
                    Console.ForegroundColor = previous;
                }
            }
            catch { }
        }
    }

    /// <summary>
    /// <summary>
    /// Returns the HTTP status code from the log entry, if present.
    /// Upstream code should always set LogWriteEntry.StatusCode when logging HTTP responses.
    /// </summary>
    /// <param name="entry">The log entry.</param>
    /// <returns>Status code if present, otherwise null.</returns>
    private static int? TryExtractHttpStatusCode(LogWriteEntry entry) =>
        // Always prefer the direct StatusCode property. Upstream code should set this explicitly.
        entry.StatusCode;

    /// <summary>
    /// Disposes log writers and clears logger cache.
    /// </summary>
    public void Dispose() {
        try { _jsonWriter?.Dispose(); } catch { }
        try { _csvWriter?.Dispose(); } catch { }
        _loggers.Clear();
        GC.SuppressFinalize(this);
    }

}
