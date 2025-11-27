namespace FileWatchRest.Models;

public sealed class FileNotification {
    public string Path { get; set; } = string.Empty;
    public string? Content { get; set; }
    public string ComputerName { get; set; } = Environment.MachineName;
    public long? FileSize { get; set; }
    public DateTime? LastWriteTime { get; set; }
}
