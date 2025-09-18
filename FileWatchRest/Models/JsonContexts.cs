using System.Text.Json.Serialization;
using FileWatchRest.Configuration;
using FileWatchRest.Services;

namespace FileWatchRest.Models;

// Diagnostic response models
public class DiagnosticStatus
{
    public IReadOnlyCollection<string> ActiveWatchers { get; set; } = [];
    public IReadOnlyDictionary<string, int> RestartAttempts { get; set; } = new Dictionary<string, int>();
    public IReadOnlyCollection<FileEventRecord> RecentEvents { get; set; } = [];
    public DateTimeOffset Timestamp { get; set; }
    public int EventCount { get; set; }
}

public class HealthStatus
{
    public string Status { get; set; } = "healthy";
    public DateTimeOffset Timestamp { get; set; }
}

public class ErrorResponse
{
    public string Error { get; set; } = string.Empty;
    public string[] AvailableEndpoints { get; set; } = [];
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(FileNotification))]
[JsonSerializable(typeof(ExternalConfiguration))]
[JsonSerializable(typeof(DiagnosticStatus))]
[JsonSerializable(typeof(HealthStatus))]
[JsonSerializable(typeof(ErrorResponse))]
[JsonSerializable(typeof(FileEventRecord))]
[JsonSerializable(typeof(FileEventRecord[]))]
[JsonSerializable(typeof(string[]))]
internal partial class MyJsonContext : JsonSerializerContext
{
}
