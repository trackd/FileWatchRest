namespace FileWatchRest.Logging;

public sealed class SimpleFileLoggerProvider : ILoggerProvider
{
    private readonly object _sync = new();
    private readonly ConcurrentDictionary<string, SimpleFileLogger> _loggers = new();
    private StreamWriter? _jsonWriter;
    private StreamWriter? _csvWriter;

    public SimpleFileLoggerOptions Options { get; }

    public SimpleFileLoggerProvider(SimpleFileLoggerOptions options)
    {
        Options = options;
        EnsureWriters();
    }

    public ILogger CreateLogger(string categoryName)
    {
        return _loggers.GetOrAdd(categoryName, name => new SimpleFileLogger(name, this));
    }

    private void EnsureWriters()
    {
        try
        {
            // Resolve a base pattern from the configured FilePathPattern. If the pattern is relative, store logs under the common AppData service directory.
            var serviceDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "FileWatchRest");
            var pattern = Options.FilePathPattern ?? "logs/FileWatchRest_{0:yyyyMMdd_HHmms}";
            if (!Path.IsPathRooted(pattern))
            {
                pattern = Path.Combine(serviceDir, pattern);
            }
            // If pattern contains a format placeholder we timestamp it per-run.
            var basePath = pattern.Contains("{") ? string.Format(pattern, DateTime.Now) : pattern;

            string EnsureFileName(string baseP, string ext)
            {
                return Path.HasExtension(baseP) ? baseP : baseP + ext;
            }

            // JSON writer
            if (Options.LogType == LogType.Json || Options.LogType == LogType.Both)
            {
                var jsonPath = EnsureFileName(basePath, ".ndjson");
                var dir = Path.GetDirectoryName(jsonPath) ?? ".";
                _ = Directory.CreateDirectory(dir);
                try { PurgeOldLogFilesForPattern(dir, Options.FilePathPattern + ".ndjson", Options.RetainedFileCountLimit); } catch { }
                _jsonWriter = File.AppendText(jsonPath);
                _jsonWriter.AutoFlush = true;
            }

            // CSV writer
            if (Options.LogType == LogType.Csv || Options.LogType == LogType.Both)
            {
                var csvPath = EnsureFileName(basePath, ".csv");
                var dir = Path.GetDirectoryName(csvPath) ?? ".";
                Directory.CreateDirectory(dir);
                try
                {
                    PurgeOldLogFilesForPattern(dir, Options.FilePathPattern + ".csv", Options.RetainedFileCountLimit);
                }
                catch { }

                // Minimal expected header (do not force extra columns)
                const string expectedHeader = "Timestamp,Level,Message,Category,Exception,StatusCode";

                // If the file exists but its header does not match the expected minimal header, replace the header using a temp file and replace.
                if (File.Exists(csvPath) && new FileInfo(csvPath).Length > 0)
                {
                    try
                    {
                        // Open original file and find the position immediately after the first newline
                        using var inputFs = new FileStream(csvPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                        long copyStart = -1;
                        const int BufSize = 8192;
                        var buf = new byte[BufSize];
                        long total = 0;
                        int read;
                        while ((read = inputFs.Read(buf, 0, BufSize)) > 0)
                        {
                            for (int i = 0; i < read; i++)
                            {
                                if (buf[i] == (byte)'\n')
                                {
                                    copyStart = total + i + 1; // position after newline
                                    break;
                                }
                            }
                            if (copyStart != -1) break;
                            total += read;
                        }

                        if (copyStart == -1) // no newline found - original file is a single line (likely just the old header)
                            copyStart = inputFs.Length;

                        var tempPath = Path.Combine(dir, $"{Guid.NewGuid():N}.tmp");

                        // Create temp file and write expected header then copy remaining bytes from original (excluding original header)
                        using (var outFs = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        using (var writer = new StreamWriter(outFs, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
                        {
                            writer.WriteLine(expectedHeader);
                            writer.Flush();

                            // Copy remainder from original starting at the byte position after the first newline
                            inputFs.Position = copyStart;
                            inputFs.CopyTo(outFs);
                        }

                        try
                        {
                            File.Replace(tempPath, csvPath, null);
                        }
                        catch
                        {
                            // If Replace fails, fallback to delete+move (best-effort)
                            try { File.Delete(csvPath); } catch { }
                            try { File.Move(tempPath, csvPath); } catch { }
                        }
                        finally
                        {
                            // Cleanup any stray temp file
                            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                        }
                    }
                    catch
                    {
                        // If anything goes wrong, fall back to leaving the file untouched and continue.
                    }
                }

                _csvWriter = File.AppendText(csvPath);
                _csvWriter.AutoFlush = true;
                if (_csvWriter.BaseStream.Length == 0)
                {
                    try { _csvWriter.WriteLine(expectedHeader); } catch (Exception ex) { Console.Error.WriteLine("Failed to write CSV header: " + ex); }
                }
            }
        }
        catch
        {
            // Don't let logging initialization crash the app
        }
    }

    private static void PurgeOldLogFilesForPattern(string directory, string configuredPathPattern, int keepLimit)
    {
        if (keepLimit <= 0) return;

        // Determine prefix and suffix for matching files.
        // If configuredPathPattern contains a formatting placeholder like {0:...}, use the text before the '{' as prefix and the text after the '}' as suffix.
        string fileNamePattern = Path.GetFileName(configuredPathPattern);
        string prefix, suffix;
        var openBrace = fileNamePattern.IndexOf('{');
        var closeBrace = fileNamePattern.IndexOf('}');
        if (openBrace >= 0 && closeBrace > openBrace)
        {
            prefix = fileNamePattern.Substring(0, openBrace);
            suffix = fileNamePattern.Substring(closeBrace + 1);
        }
        else
        {
            // No format placeholder - use full file name as exact match (we still allow variants like timestamped copies with suffix)
            var ext = Path.GetExtension(fileNamePattern);
            var nameOnly = Path.GetFileNameWithoutExtension(fileNamePattern);
            prefix = nameOnly;
            suffix = ext;
        }

        var files = Directory.EnumerateFiles(directory)
            .Where(f =>
            {
                var fn = Path.GetFileName(f);
                return fn.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && fn.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
            })
            .Select(f => new FileInfo(f))
            .OrderByDescending(fi => fi.LastWriteTimeUtc)
            .ToList();

        if (files.Count <= keepLimit) return;

        // Keep newest 'keepLimit' files, delete the rest
        foreach (var toDelete in files.Skip(keepLimit))
        {
            try
            {
                toDelete.Delete();
            }
            catch
            {
                // Ignore deletion errors to avoid startup failures
            }
        }
    }

    internal void WriteLine(LogWriteEntry entry)
    {
        lock (_sync)
        {
            if ((Options.LogType == LogType.Json || Options.LogType == LogType.Both) && _jsonWriter is not null)
            {
                try
                {
                    // Use source-generated serializer (JsonSerializerContext) to be AOT/trimming-safe
                    var json = JsonSerializer.Serialize(entry, MyJsonContext.Default.LogWriteEntry);
                    _jsonWriter.WriteLine(json);
                }
                catch { }
            }

            if ((Options.LogType == LogType.Csv || Options.LogType == LogType.Both) && _csvWriter is not null)
            {
                try
                {
                    // Simple CSV escape
                    string Escape(string s) => s?.Replace("\"", "\"\"").Replace("\r", " ").Replace("\n", " ") ?? string.Empty;
                    var line = $"{Escape(entry.Timestamp.ToString("o"))},{Escape(entry.Level)},{Escape(entry.Message)},{Escape(entry.FriendlyCategory)},{Escape(entry.Exception)},{(entry.StatusCode.HasValue ? entry.StatusCode.ToString() : string.Empty)}";
                    _csvWriter.WriteLine(line);
                }
                catch { }
            }

            // Also write a concise human-readable console line to make logs easier for operators to scan
            try
            {
                var consoleMsg = $"{entry.Timestamp:yyyy-MM-dd HH:mm:ss} {entry.Level,-7} {entry.FriendlyCategory}: {entry.Message}";
                if (entry.EventId != 0) consoleMsg += $" (EventId:{entry.EventId})";
                // Do not include entry.Properties in console output; structured properties are written to JSON file only
                if (!string.IsNullOrEmpty(entry.Exception)) consoleMsg += $" => {entry.Exception}";

                // Determine color: Information is white by default; treat successful operations (2xx or explicit success messages) as green.
                var previous = Console.ForegroundColor;
                try
                {
                    ConsoleColor color;

                    // Try to detect an HTTP status code in message/properties
                    int? statusCode = entry.StatusCode ?? TryExtractHttpStatusCode(entry);
                    bool isExplicitSuccessMessage = entry.Message?.IndexOf("Successfully posted", StringComparison.OrdinalIgnoreCase) >= 0
                        || entry.Message?.IndexOf("successfully posted file", StringComparison.OrdinalIgnoreCase) >= 0
                        || entry.Message?.IndexOf("upload succeeded", StringComparison.OrdinalIgnoreCase) >= 0;

                    if (string.Equals(entry.Level, "Information", StringComparison.OrdinalIgnoreCase))
                    {
                        if (statusCode.HasValue)
                        {
                            if (statusCode.Value >= 200 && statusCode.Value <= 299)
                                color = ConsoleColor.Green;
                            else if (statusCode.Value >= 400 && statusCode.Value <= 499)
                                color = ConsoleColor.Yellow;
                            else if (statusCode.Value >= 500 && statusCode.Value <= 599)
                                color = ConsoleColor.Red;
                            else
                                color = ConsoleColor.White;
                        }
                        else if (isExplicitSuccessMessage)
                        {
                            color = ConsoleColor.Green;
                        }
                        else
                        {
                            color = ConsoleColor.White; // Default info color is white
                        }
                    }
                    else
                    {
                        switch (entry.Level)
                        {
                            case "Trace": color = ConsoleColor.DarkGray; break;
                            case "Debug": color = ConsoleColor.Gray; break;
                            case "Warning": color = ConsoleColor.Yellow; break;
                            case "Error": color = ConsoleColor.Red; break;
                            case "Critical": color = ConsoleColor.Magenta; break;
                            default: color = ConsoleColor.White; break;
                        }
                    }

                    Console.ForegroundColor = color;
                    Console.WriteLine(consoleMsg);
                }
                finally
                {
                    Console.ForegroundColor = previous;
                }
            }
            catch { }
        }
    }

    private static int? TryExtractHttpStatusCode(LogWriteEntry entry)
    {
        // Prefer parsing the structured 'Properties' payload (it's emitted as a JSON-like string) instead of regexing.
        if (!string.IsNullOrEmpty(entry.Properties))
        {
            try
            {
                using var doc = JsonDocument.Parse(entry.Properties);
                if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("StatusCode", out var element))
                {
                    if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var num))
                        return num;

                    if (element.ValueKind == JsonValueKind.String)
                    {
                        var str = element.GetString();
                        if (int.TryParse(str, out var parsed)) return parsed;
                    }
                }
            }
            catch
            {
                // If properties are not valid JSON, fall back to message regex below
            }
        }

        // Look for a 3-digit HTTP status code in the message as a fallback
        if (!string.IsNullOrEmpty(entry.Message))
        {
            var m = Regex.Match(entry.Message, @"\b(1\d{2}|2\d{2}|3\d{2}|4\d{2}|5\d{2})\b");
            if (m.Success && int.TryParse(m.Value, out var v)) return v;
        }

        return null;
    }

    public void Dispose()
    {
        try { _jsonWriter?.Dispose(); } catch { }
        try { _csvWriter?.Dispose(); } catch { }
        _loggers.Clear();
    }
}
