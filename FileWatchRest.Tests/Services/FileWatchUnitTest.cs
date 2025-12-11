namespace FileWatchRest.Tests;

// Restored tests originally from FileWatchRest.Tests/FileWatchUnitTest.cs on main branch.
// These verify core models and light integration helpers and are placed under Services.

public class ExternalConfigurationTests {
    [Fact]
    public void Constructor_SetsDefaultValues() {
        var config = new ExternalConfiguration();

        Assert.Empty(config.Folders);
        Assert.Null(config.ApiEndpoint);
        Assert.Null(config.BearerToken);
        Assert.False(config.PostFileContents);
        Assert.False(config.MoveProcessedFiles);
        Assert.Equal("processed", config.ProcessedFolder);
    }

    [Fact]
    public void Properties_CanBeSetAndRetrieved() {
        var config = new ExternalConfiguration();
        var expectedFolders = new List<ExternalConfiguration.WatchedFolderConfig> {
            new() { FolderPath = @"C:\temp\test1", ActionName = "Default" },
            new() { FolderPath = @"C:\temp\test2", ActionName = "Default" }
        };

        config.Folders = expectedFolders;
        config.ApiEndpoint = "https://api.example.com/webhook";
        config.PostFileContents = true;

        Assert.Equivalent(expectedFolders, config.Folders);
        Assert.Equal("https://api.example.com/webhook", config.ApiEndpoint);
        Assert.True(config.PostFileContents);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    [InlineData(1000)]
    public void DebounceMilliseconds_AcceptsValues(int debounceMs) {
        var config = new ExternalConfiguration { DebounceMilliseconds = debounceMs };
        Assert.Equal(debounceMs, config.DebounceMilliseconds);
    }
}

public class FileNotificationTests {
    [Fact]
    public void Constructor_CreatesEmptyFileNotification() {
        var notification = new FileNotification();
        Assert.Empty(notification.Path);
    }

    [Fact]
    public void JsonSerialization_SerializesCorrectly() {
        var notification = new FileNotification { Path = @"C:\temp\example.txt" };
        string json = JsonSerializer.Serialize(notification);
        FileNotification? deserialized = JsonSerializer.Deserialize<FileNotification>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(@"C:\temp\example.txt", deserialized!.Path);
    }
}

public class DiagnosticsServiceTests : IDisposable {
    private readonly Mock<ILogger<DiagnosticsService>> _mockLogger;
    private readonly Mock<IOptionsMonitor<ExternalConfiguration>> _mockOptionsMonitor;
    private readonly DiagnosticsService _diagnosticsService;

    public DiagnosticsServiceTests() {
        _mockLogger = new Mock<ILogger<DiagnosticsService>>();
        _mockOptionsMonitor = new Mock<IOptionsMonitor<ExternalConfiguration>>();
        _mockOptionsMonitor.SetupGet(m => m.CurrentValue).Returns(new ExternalConfiguration());
        _diagnosticsService = new DiagnosticsService(_mockLogger.Object, _mockOptionsMonitor.Object);
    }

    [Fact]
    public void Constructor_InitializesCorrectly() {
        Assert.NotNull(_diagnosticsService);
        Assert.Empty(_diagnosticsService.GetActiveWatchers());
    }

    [Fact]
    public void RegisterWatcher_AddsWatcherToActiveList() {
        string folderPath = @"C:\temp\test";
        _diagnosticsService.RegisterWatcher(folderPath);
        Assert.Contains(folderPath, _diagnosticsService.GetActiveWatchers());
    }

    public void Dispose() {
        _diagnosticsService?.Dispose();
        GC.SuppressFinalize(this);
    }
}
