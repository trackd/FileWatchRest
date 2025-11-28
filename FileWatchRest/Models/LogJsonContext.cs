namespace FileWatchRest.Models;

// Compact JSON context used for newline-delimited log entries (NDJSON).
[JsonSourceGenerationOptions(WriteIndented = false, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(LogWriteEntry))]
internal partial class MyJsonLogContext : JsonSerializerContext { }
