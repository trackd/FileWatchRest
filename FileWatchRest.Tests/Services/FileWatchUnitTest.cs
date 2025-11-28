namespace FileWatchRest.Tests;

// Restored tests originally from FileWatchRest.Tests/FileWatchUnitTest.cs on main branch.
// These verify core models and light integration helpers and are placed under Services.

public class ExternalConfigurationTests {
    [Fact]
    public void Constructor_SetsDefaultValues() {
        var config = new ExternalConfiguration();

        config.Folders.Should().BeEmpty();
        config.ApiEndpoint.Should().BeNull();
        config.BearerToken.Should().BeNull();
        config.PostFileContents.Should().BeFalse();
        config.MoveProcessedFiles.Should().BeFalse();
        config.ProcessedFolder.Should().Be("processed");
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

        config.Folders.Should().BeEquivalentTo(expectedFolders);
        config.ApiEndpoint.Should().Be("https://api.example.com/webhook");
        config.PostFileContents.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    [InlineData(1000)]
    public void DebounceMilliseconds_AcceptsValues(int debounceMs) {
        var config = new ExternalConfiguration { DebounceMilliseconds = debounceMs };
        config.DebounceMilliseconds.Should().Be(debounceMs);
    }
}

public class FileNotificationTests {
    [Fact]
    public void Constructor_CreatesEmptyFileNotification() {
        var notification = new FileNotification();
        notification.Path.Should().BeEmpty();
    }

    [Fact]
    public void JsonSerialization_SerializesCorrectly() {
        var notification = new FileNotification { Path = @"C:\temp\example.txt" };
        string json = JsonSerializer.Serialize(notification);
        FileNotification? deserialized = JsonSerializer.Deserialize<FileNotification>(json);

        deserialized.Should().NotBeNull();
        deserialized!.Path.Should().Be(@"C:\temp\example.txt");
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
        _diagnosticsService.Should().NotBeNull();
        _diagnosticsService.GetActiveWatchers().Should().BeEmpty();
    }

    [Fact]
    public void RegisterWatcher_AddsWatcherToActiveList() {
        string folderPath = @"C:\temp\test";
        _diagnosticsService.RegisterWatcher(folderPath);
        _diagnosticsService.GetActiveWatchers().Should().Contain(folderPath);
    }

    public void Dispose() {
        _diagnosticsService?.Dispose();
        GC.SuppressFinalize(this);
    }
}
