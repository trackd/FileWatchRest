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

    [Fact]
    public async Task ConfigEndpointReturnsFullConfiguration_RuntimeLiveConfig() {
        // Arrange - create a live config and set it on the diagnostics service
        var cfg = new ExternalConfiguration {
            Folders = [new() { FolderPath = "C:\\tmp", ActionName = "a1" }],
            Actions = [new() { Name = "a1", ActionType = ExternalConfiguration.FolderActionType.RestPost, ApiEndpoint = "https://api.test/" }],
            ApiEndpoint = "https://api.default/"
        };

        // Use a simple options monitor mock that returns the cfg
        var optionsMock = new TestUtilities.OptionsMonitorMock<ExternalConfiguration>();
        optionsMock.SetCurrentValue(cfg);
        var diag = new DiagnosticsService(_mockLogger.Object, optionsMock);
        diag.SetConfiguration(cfg);
        diag.SetBearerToken(null);

        // pick a free port
        int port;
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        string prefix = $"http://localhost:{port}/";
        diag.StartHttpServer(prefix);

        using var client = new HttpClient();
        HttpResponseMessage resp = await client.GetAsync(prefix + "config");
        string body = await resp.Content.ReadAsStringAsync();

        Assert.True(resp.IsSuccessStatusCode);
        Assert.Contains("Folders", body);
        Assert.Contains("Actions", body);
        Assert.Contains("a1", body);

        diag.Dispose();
    }
}
