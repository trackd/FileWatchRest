namespace FileWatchRest.Logging;

public sealed class LogWriteEntry {
    public DateTime Timestamp { get; set; }
    public string Level { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Exception { get; set; } = string.Empty;
    public int EventId { get; set; }
    /// <summary>
    /// Structured properties emitted as a JSON object in NDJSON output
    /// </summary>
    public Dictionary<string, string?> Properties { get; set; } = [];
    public string MachineName { get; set; } = string.Empty;
    public int ProcessId { get; set; }
    public string FriendlyCategory { get; set; } = string.Empty;

    /// <summary>
    /// Optional structured HTTP status code, populated if present in structured state
    /// </summary>
    public int? StatusCode { get; set; }
}
