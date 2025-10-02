namespace FileWatchRest.Logging;

public sealed class LogWriteEntry
{
    public DateTime Timestamp { get; set; }
    public string Level { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Exception { get; set; } = string.Empty;
    public int EventId { get; set; }
    public string Properties { get; set; } = string.Empty; // simple JSON-ish string of structured properties
    public string MachineName { get; set; } = string.Empty;
    public int ProcessId { get; set; }
    public string FriendlyCategory { get; set; } = string.Empty;

    // Optional structured HTTP status code, populated if present in structured state
    public int? StatusCode { get; set; }
}
