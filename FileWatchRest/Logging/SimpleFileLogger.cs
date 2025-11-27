namespace FileWatchRest.Logging;

/// <summary>
/// Implements a simple file logger for structured and unstructured log output.
/// </summary>
/// <remarks>
/// Supports JSON and CSV output, with friendly category formatting and concise exception output.
/// </remarks>
/// <remarks>
/// Initializes a new instance of the <see cref="SimpleFileLogger"/> class.
/// </remarks>
/// <param name="category">The logger category name.</param>
/// <param name="provider">The file logger provider.</param>
internal sealed class SimpleFileLogger(string category, SimpleFileLoggerProvider provider) : ILogger {
    private readonly string _category = category;
    private readonly SimpleFileLoggerProvider _provider = provider;

    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel) => logLevel >= _provider.Options.LogLevel;

    /// <inheritdoc />
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) {
        if (!IsEnabled(logLevel)) {
            return;
        }

        string message = formatter(state, exception);

        int? statusCode = null;
        var propertiesDict = new Dictionary<string, string?>();

        // Optimize structured logging extraction into a structured dictionary
        if (state is IEnumerable<KeyValuePair<string, object>> kvps) {
            foreach (KeyValuePair<string, object> kv in kvps) {
                string key = kv.Key ?? string.Empty;
                if (key == "{OriginalFormat}") {
                    continue;
                }

                string? val = kv.Value?.ToString();
                if (key == "StatusCode" && int.TryParse(val, out int parsed)) {
                    statusCode = parsed;
                }

                propertiesDict[key] = val;
            }
        }

        // Write log entry
        try {
            string friendly = GetFriendlyCategory(_category);
            _provider.WriteLine(new LogWriteEntry {
                Timestamp = DateTime.Now,
                Level = logLevel.ToString(),
                Category = _category,
                FriendlyCategory = friendly,
                Message = message,
                Exception = FormatException(exception),
                EventId = eventId.Id,
                Properties = propertiesDict,
                MachineName = Environment.MachineName,
                ProcessId = Environment.ProcessId,
                StatusCode = statusCode
            });
        }
        catch {
            // Swallow logging exceptions to avoid cascading failures.
        }
    }

    /// <summary>
    /// Formats exceptions for log output, providing concise type and message information.
    /// </summary>
    /// <param name="exception">The exception to format.</param>
    /// <returns>Formatted exception string.</returns>
    private static string FormatException(Exception? exception) {
        if (exception is null) {
            return string.Empty;
        }

        // For common exceptions, provide concise output without full stack traces
        var result = new StringBuilder();
        // Use pattern matching to avoid GetType() trimming issues
        string typeName = GetExceptionTypeName(exception);
        _ = result.Append(typeName).Append(": ").Append(exception.Message);

        // For inner exceptions, add just the type and message (no stack)
        Exception? inner = exception.InnerException;
        if (inner is not null) {
            string innerTypeName = GetExceptionTypeName(inner);
            _ = result.Append(" --> ").Append(innerTypeName).Append(": ").Append(inner.Message);
        }

        return result.ToString();
    }

    /// <summary>
    /// Gets a trim-safe exception type name for logging.
    /// </summary>
    /// <param name="exception">The exception instance.</param>
    /// <returns>Type name string.</returns>
    private static string GetExceptionTypeName(Exception exception) =>
        exception switch {
            HttpRequestException => nameof(HttpRequestException),
            TimeoutException => nameof(TimeoutException),
            TaskCanceledException => nameof(TaskCanceledException),
            OperationCanceledException => nameof(OperationCanceledException),
            IOException => nameof(IOException),
            UnauthorizedAccessException => nameof(UnauthorizedAccessException),
            ArgumentException => nameof(ArgumentException),
            InvalidOperationException => nameof(InvalidOperationException),
            NotSupportedException => nameof(NotSupportedException),
            _ => "Exception" // Trim-safe fallback
        };

    /// <summary>
    /// Escapes special characters for safe log output.
    /// </summary>
    /// <param name="input">Input string.</param>
    /// <returns>Escaped string.</returns>
    private static string Escape(string input) =>
        input?.Replace("\\", "\\\\").Replace("\"", "\\\"") ?? string.Empty;

    /// <summary>
    /// Returns a friendly category name for log output, simplifying known patterns.
    /// </summary>
    /// <param name="category">The logger category string.</param>
    /// <returns>Friendly category string.</returns>
    private static string GetFriendlyCategory(string category) {
        if (string.IsNullOrEmpty(category)) {
            return string.Empty;
        }

        try {
            // Known pattern: System.Net.Http.HttpClient.{clientName}[.*]
            const string httpClientPrefix = "System.Net.Http.HttpClient.";
            if (category.StartsWith(httpClientPrefix, StringComparison.OrdinalIgnoreCase)) {
                string tail = category[httpClientPrefix.Length..];
                // tail often looks like "fileApi.ClientHandler" or "fileApi.LogicalHandler"
                string[] parts = tail.Split('.', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 0) {
                    string clientName = parts[0];
                    string remainder = parts.Length > 1 ? string.Join('.', parts.Skip(1)) : string.Empty;
                    return string.IsNullOrEmpty(remainder) ? $"HttpClient({clientName})" : $"HttpClient({clientName}).{remainder}";
                }
            }

            // PollyPolicies or application categories keep short last segment(s)
            string[] tokens = category.Split('.', StringSplitOptions.RemoveEmptyEntries);
            return tokens.Length >= 2 ? string.Join('.', tokens.Skip(Math.Max(0, tokens.Length - 2))) : category;
        }
        catch {
            return category;
        }
    }
}
