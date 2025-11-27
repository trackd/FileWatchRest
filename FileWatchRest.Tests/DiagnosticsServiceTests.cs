namespace FileWatchRest.Tests;

public class DiagnosticsServiceTests2 : IDisposable {
    private readonly DiagnosticsService _service;
    private readonly Mock<ILogger<DiagnosticsService>> _mockLogger = new();

    public DiagnosticsServiceTests2() {
        var configMonitor = new TestUtilities.OptionsMonitorMock<ExternalConfiguration>();
        _service = new DiagnosticsService(_mockLogger.Object, configMonitor);
    }

    [Fact]
    public void IsFilePostedReturnsTrueForSuccess200() {
        string path = "C:/test/file.txt";
        _service.RecordFileEvent(path, true, 200);
        Assert.True(_service.IsFilePosted(path));
    }

    [Fact]
    public void IsFilePostedReturnsFalseForNon200() {
        string path = "C:/test/file.txt";
        _service.RecordFileEvent(path, true, 500);
        Assert.False(_service.IsFilePosted(path));
    }

    [Fact]
    public void IsFilePostedReturnsFalseForFailedPost() {
        string path = "C:/test/file.txt";
        _service.RecordFileEvent(path, false, 200);
        Assert.False(_service.IsFilePosted(path));
    }

    [Fact]
    public void IsFilePostedHandlesMultipleEvents() {
        string path = "C:/test/file.txt";
        _service.RecordFileEvent(path, false, 500);
        _service.RecordFileEvent(path, true, 200);
        Assert.True(_service.IsFilePosted(path));
        _service.RecordFileEvent(path, false, 200);
        Assert.False(_service.IsFilePosted(path));
    }

    [Fact]
    public void IsFilePostedReturnsFalseForUnknownPath() => Assert.False(_service.IsFilePosted("C:/unknown/file.txt"));

    public void Dispose() {
        // No unmanaged resources to clean up for this test fixture
        _service?.Dispose();
        GC.SuppressFinalize(this);
    }
}
