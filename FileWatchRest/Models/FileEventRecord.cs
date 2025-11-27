namespace FileWatchRest.Models;

public sealed class FileEventRecord {
    public string Path { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public bool PostedSuccess { get; set; }
    public int? StatusCode { get; set; }

    public FileEventRecord() { Path = string.Empty; Timestamp = DateTimeOffset.Now; PostedSuccess = false; StatusCode = null; }

    public FileEventRecord(string path, DateTimeOffset timestamp, bool postedSuccess, int? statusCode) {
        Path = path;
        Timestamp = timestamp;
        PostedSuccess = postedSuccess;
        StatusCode = statusCode;
    }
}
