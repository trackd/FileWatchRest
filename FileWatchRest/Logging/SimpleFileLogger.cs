namespace FileWatchRest.Logging;

internal sealed class SimpleFileLogger : ILogger
{
    private readonly string _category;
    private readonly SimpleFileLoggerProvider _provider;

    public SimpleFileLogger(string category, SimpleFileLoggerProvider provider)
    {
        _category = category;
        _provider = provider;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= _provider.Options.LogLevel;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;
        var message = formatter(state, exception);

        string properties = string.Empty;
        int? statusCode = null;

        try
        {
            // The state for structured logging is often an IReadOnlyList<KeyValuePair<string, object>>
            if (state is IEnumerable<KeyValuePair<string, object>> kvps)
            {
                var sb = new StringBuilder();
                sb.Append('{');
                var first = true;
                foreach (var kv in kvps)
                {
                    if (!first) sb.Append(','); first = false;
                    var key = kv.Key ?? string.Empty;

                    // Skip framework-internal formatting metadata that only repeats the template
                    if (string.Equals(key, "{OriginalFormat}", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var val = kv.Value?.ToString() ?? string.Empty;

                    // Capture StatusCode into a dedicated field when present
                    if (string.Equals(key, "StatusCode", StringComparison.OrdinalIgnoreCase))
                    {
                        if (int.TryParse(val, out var parsed)) statusCode = parsed;
                    }

                    sb.Append('"').Append(Escape(key)).Append('"').Append(':').Append('"').Append(Escape(val)).Append('"');
                }
                sb.Append('}');
                properties = sb.ToString();
            }
        }
        catch
        {
            // ignore property extraction failures
        }

        try
        {
            var friendly = GetFriendlyCategory(_category);
            _provider.WriteLine(new LogWriteEntry
            {
                Timestamp = DateTime.Now,
                Level = logLevel.ToString(),
                Category = _category,
                FriendlyCategory = friendly,
                Message = message,
                Exception = exception?.ToString() ?? string.Empty,
                EventId = eventId.Id,
                Properties = properties,
                MachineName = Environment.MachineName,
                ProcessId = Environment.ProcessId,
                StatusCode = statusCode
            });
        }
        catch
        {
            // Swallow logging exceptions to avoid cascading failures.
        }
    }

    private static string Escape(string input)
    {
        return input?.Replace("\\", "\\\\").Replace("\"", "\\\"") ?? string.Empty;
    }

    private static string GetFriendlyCategory(string category)
    {
        if (string.IsNullOrEmpty(category)) return string.Empty;
        try
        {
            // Known pattern: System.Net.Http.HttpClient.{clientName}[.*]
            const string httpClientPrefix = "System.Net.Http.HttpClient.";
            if (category.StartsWith(httpClientPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var tail = category[httpClientPrefix.Length..];
                // tail often looks like "fileApi.ClientHandler" or "fileApi.LogicalHandler"
                var parts = tail.Split('.', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 0)
                {
                    var clientName = parts[0];
                    var remainder = parts.Length > 1 ? string.Join('.', parts.Skip(1)) : string.Empty;
                    return string.IsNullOrEmpty(remainder) ? $"HttpClient({clientName})" : $"HttpClient({clientName}).{remainder}";
                }
            }

            // PollyPolicies or application categories keep short last segment(s)
            var tokens = category.Split('.', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length >= 2)
            {
                return string.Join('.', tokens.Skip(Math.Max(0, tokens.Length - 2)));
            }

            return category;
        }
        catch
        {
            return category;
        }
    }
}
