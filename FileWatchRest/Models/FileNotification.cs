namespace FileWatchRest.Models;

public sealed class FileNotification
{
    public string Path { get; set; } = string.Empty;
    public string? Contents { get; set; }
    public long? FileSize { get; set; }
    public DateTime? LastWriteTime { get; set; }
}
