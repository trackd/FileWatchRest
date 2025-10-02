namespace FileWatchRest.Models;

public sealed class UploadMetadata
{
    public string Path { get; set; } = string.Empty;
    public long? FileSize { get; set; }
    public DateTime? LastWriteTime { get; set; }
}
