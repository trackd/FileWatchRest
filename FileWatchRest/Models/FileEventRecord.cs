namespace FileWatchRest.Models;

public sealed class FileEventRecord {
    public string Path { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public bool PostedSuccess { get; set; }
    public int? StatusCode { get; set; }
    public string? FileHash { get; set; }

    public FileEventRecord() {
        Path = string.Empty;
        Timestamp = DateTimeOffset.Now;
        PostedSuccess = false;
        StatusCode = null;
        FileHash = string.Empty;
    }

    public FileEventRecord(string path, DateTimeOffset timestamp, bool postedSuccess, int? statusCode, string? fileHash = null) {
        Path = path;
        Timestamp = timestamp;
        PostedSuccess = postedSuccess;
        StatusCode = statusCode;
        FileHash = fileHash ?? string.Empty;
    }
}
