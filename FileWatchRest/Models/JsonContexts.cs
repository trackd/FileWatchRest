namespace FileWatchRest.Models;

/// <summary>
/// Diagnostic response models
/// </summary>
public class DiagnosticStatus {
    public IReadOnlyCollection<string> ActiveWatchers { get; set; } = [];
    public IReadOnlyDictionary<string, int> RestartAttempts { get; set; } = new Dictionary<string, int>();
    public IReadOnlyCollection<FileEventRecord> RecentEvents { get; set; } = [];
    public DateTimeOffset Timestamp { get; set; }
    public int EventCount { get; set; }
    public IReadOnlyDictionary<string, CircuitStateInfo> CircuitStates { get; set; } = new Dictionary<string, CircuitStateInfo>();
    public int TotalEvents { get; set; }
}

public class HealthStatus {
    public string Status { get; set; } = "healthy";
    public DateTimeOffset Timestamp { get; set; }
}

public class ErrorResponse {
    public string Error { get; set; } = string.Empty;
    public string[] AvailableEndpoints { get; set; } = [];
}

[JsonSourceGenerationOptions(WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(UploadMetadata))]
[JsonSerializable(typeof(FileNotification))]
[JsonSerializable(typeof(ExternalConfiguration))]
[JsonSerializable(typeof(ExternalConfiguration.WatchedFolderConfig))]
[JsonSerializable(typeof(ExternalConfiguration.ActionConfig))]
[JsonSerializable(typeof(ExternalConfiguration.FolderActionType))]
[JsonSerializable(typeof(SimpleFileLoggerOptions))]
[JsonSerializable(typeof(LogType))]
[JsonSerializable(typeof(LogWriteEntry))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(CircuitStateInfo))]
[JsonSerializable(typeof(Dictionary<string, CircuitStateInfo>))]
[JsonSerializable(typeof(DiagnosticStatus))]
[JsonSerializable(typeof(HealthStatus))]
[JsonSerializable(typeof(ErrorResponse))]
[JsonSerializable(typeof(FileEventRecord))]
[JsonSerializable(typeof(FileEventRecord[]))]
[JsonSerializable(typeof(string[]))]
internal partial class MyJsonContext : JsonSerializerContext {
}
