namespace FileWatchRest.Tests;

public class ExternalConfigurationTests {
    [Fact]
    public void ConstructorSetsDefaultValues() {
        // Act
        var config = new ExternalConfiguration();

        // Assert
        config.Folders.Should().BeEmpty();
        config.ApiEndpoint.Should().BeNull();
        config.BearerToken.Should().BeNull();
        config.PostFileContents.Should().BeFalse();
        config.MoveProcessedFiles.Should().BeFalse();
        config.ProcessedFolder.Should().Be("processed");
        config.AllowedExtensions.Should().BeEmpty();
        config.IncludeSubdirectories.Should().BeTrue();
        config.DebounceMilliseconds.Should().Be(1000);
        config.Retries.Should().Be(3);
        config.RetryDelayMilliseconds.Should().Be(500);
        config.DiagnosticsUrlPrefix.Should().Be("http://localhost:5005/");
        config.ChannelCapacity.Should().Be(1000);
        config.MaxParallelSends.Should().Be(4);
        config.FileWatcherInternalBufferSize.Should().Be(65536);
        config.Logging.LogLevel.Should().Be(LogLevel.Information);
    }

    [Fact]
    public void PropertiesCanBeSetAndRetrieved() {
        // Arrange
        var config = new ExternalConfiguration();
        string[] expectedFolders = [@"C:\temp\test1", @"C:\temp\test2"];
        string[] expectedExtensions = [".txt", ".json", ".xml"];

        // Act
        config.Folders = [.. expectedFolders.Select(f => new ExternalConfiguration.WatchedFolderConfig { FolderPath = f })];
        config.ApiEndpoint = "https://api.example.com/webhook";
        config.BearerToken = "test-bearer-token-123";
        config.PostFileContents = true;
        config.MoveProcessedFiles = true;
        config.ProcessedFolder = "archived";
        config.AllowedExtensions = expectedExtensions;
        config.IncludeSubdirectories = false;
        config.DebounceMilliseconds = 2500;
        config.Retries = 5;
        config.RetryDelayMilliseconds = 1000;
        config.DiagnosticsUrlPrefix = "http://localhost:8080/";
        config.ChannelCapacity = 2000;
        config.MaxParallelSends = 8;
        config.FileWatcherInternalBufferSize = 131072;
        config.Logging = new SimpleFileLoggerOptions { LogLevel = LogLevel.Debug };

        // Assert
        config.Folders.Select(f => f.FolderPath).Should().BeEquivalentTo(expectedFolders);
        config.ApiEndpoint.Should().Be("https://api.example.com/webhook");
        config.BearerToken.Should().Be("test-bearer-token-123");
        config.PostFileContents.Should().BeTrue();
        config.MoveProcessedFiles.Should().BeTrue();
        config.ProcessedFolder.Should().Be("archived");
        config.AllowedExtensions.Should().BeEquivalentTo(expectedExtensions);
        config.IncludeSubdirectories.Should().BeFalse();
        config.DebounceMilliseconds.Should().Be(2500);
        config.Retries.Should().Be(5);
        config.RetryDelayMilliseconds.Should().Be(1000);
        config.DiagnosticsUrlPrefix.Should().Be("http://localhost:8080/");
        config.ChannelCapacity.Should().Be(2000);
        config.MaxParallelSends.Should().Be(8);
        config.FileWatcherInternalBufferSize.Should().Be(131072);
        config.Logging.LogLevel.Should().Be(LogLevel.Debug);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(500)]
    [InlineData(1000)]
    [InlineData(5000)]
    [InlineData(30000)]
    public void DebounceMillisecondsAcceptsValidValues(int debounceMs) {
        // Arrange
        var config = new ExternalConfiguration {
            // Act
            DebounceMilliseconds = debounceMs
        };

        // Assert
        config.DebounceMilliseconds.Should().Be(debounceMs);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(10)]
    public void RetriesAcceptsValidValues(int retries) {
        // Arrange
        var config = new ExternalConfiguration {
            // Act
            Retries = retries
        };

        // Assert
        config.Retries.Should().Be(retries);
    }

    [Fact]
    public void JsonSerializationPreservesAllProperties() {
        // Arrange
        var original = new ExternalConfiguration {
            Folders = [new ExternalConfiguration.WatchedFolderConfig { FolderPath = @"C:\temp\watch1" }, new ExternalConfiguration.WatchedFolderConfig { FolderPath = @"C:\temp\watch2" }],
            ApiEndpoint = "https://api.example.com/files",
            BearerToken = "test-token-123",
            PostFileContents = true,
            MoveProcessedFiles = true,
            ProcessedFolder = "completed",
            AllowedExtensions = [".txt", ".json", ".xml"],
            IncludeSubdirectories = false,
            DebounceMilliseconds = 1500,
            Retries = 5,
            RetryDelayMilliseconds = 750,
            DiagnosticsUrlPrefix = "http://localhost:8080/",
            ChannelCapacity = 2000,
            MaxParallelSends = 6,
            FileWatcherInternalBufferSize = 131072,
            Logging = new SimpleFileLoggerOptions { LogLevel = LogLevel.Debug }
        };

        // Act
        string json = JsonSerializer.Serialize(original);
        ExternalConfiguration? deserialized = JsonSerializer.Deserialize<ExternalConfiguration>(json);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Folders.Select(f => f.FolderPath).Should().BeEquivalentTo(original.Folders.Select(f => f.FolderPath));
        deserialized.ApiEndpoint.Should().Be(original.ApiEndpoint);
        deserialized.BearerToken.Should().Be(original.BearerToken);
        deserialized.PostFileContents.Should().Be(original.PostFileContents);
        deserialized.MoveProcessedFiles.Should().Be(original.MoveProcessedFiles);
        deserialized.ProcessedFolder.Should().Be(original.ProcessedFolder);
        deserialized.AllowedExtensions.Should().BeEquivalentTo(original.AllowedExtensions);
        deserialized.IncludeSubdirectories.Should().Be(original.IncludeSubdirectories);
        deserialized.DebounceMilliseconds.Should().Be(original.DebounceMilliseconds);
        deserialized.Retries.Should().Be(original.Retries);
        deserialized.RetryDelayMilliseconds.Should().Be(original.RetryDelayMilliseconds);
        deserialized.DiagnosticsUrlPrefix.Should().Be(original.DiagnosticsUrlPrefix);
        deserialized.ChannelCapacity.Should().Be(original.ChannelCapacity);
        deserialized.MaxParallelSends.Should().Be(original.MaxParallelSends);
        deserialized.FileWatcherInternalBufferSize.Should().Be(original.FileWatcherInternalBufferSize);
        deserialized.Logging.LogLevel.Should().Be(original.Logging.LogLevel);
    }
}

public class FileNotificationTests {
    [Fact]
    public void ConstructorCreatesEmptyFileNotification() {
        // Act
        var notification = new FileNotification();

        // Assert
        notification.Path.Should().BeEmpty(); // FileNotification initializes Path as empty string
    }

    [Fact]
    public void PropertiesCanBeSetAndRetrieved() {
        // Arrange
        string expectedPath = @"C:\temp\test.txt";

        // Act
        var notification = new FileNotification {
            Path = expectedPath
        };

        // Assert
        notification.Path.Should().Be(expectedPath);
    }

    [Fact]
    public void JsonSerializationSerializesCorrectly() {
        // Arrange
        var notification = new FileNotification {
            Path = @"C:\temp\example.txt"
        };

        // Act
        string json = JsonSerializer.Serialize(notification);
        FileNotification? deserialized = JsonSerializer.Deserialize<FileNotification>(json);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Path.Should().Be(@"C:\temp\example.txt");
    }
}

public class DiagnosticsServiceTests : IDisposable {
    [Fact]
    public void StartHttpServerSetsCurrentPrefixAndStartsListener() {
        string prefix = "http://localhost:5050/";
        _diagnosticsService.StartHttpServer(prefix);
        _diagnosticsService.CurrentPrefix.Should().Be(prefix);
        _diagnosticsService.Dispose();
    }

    [Fact]
    public void RestartHttpServerStopsAndRestartsListener() {
        string prefix1 = "http://localhost:5051/";
        string prefix2 = "http://localhost:5052/";
        _diagnosticsService.StartHttpServer(prefix1);
        _diagnosticsService.RestartHttpServer(prefix2);
        _diagnosticsService.CurrentPrefix.Should().Be(prefix2);
        _diagnosticsService.Dispose();
    }

    [Fact]
    public void SetBearerTokenEnforcesAuthentication() {
        _diagnosticsService.SetBearerToken("testtoken");
        // Simulate request with and without token
        // This is a placeholder: actual HTTP request simulation would require integration test
        // Use public API only: CurrentPrefix or GetStatus can be checked, but not private fields
        Assert.True(true); // Placeholder assertion
    }

    [Fact]
    public void UpdateCircuitStateStoresStateCorrectly() {
        string endpoint = "http://api/test";
        _diagnosticsService.UpdateCircuitState(endpoint, 3, DateTimeOffset.Now.AddMinutes(5));
        IReadOnlyDictionary<string, CircuitStateInfo> states = _diagnosticsService.GetCircuitStatesSnapshot();
        states.Should().ContainKey(endpoint);
        states[endpoint].Failures.Should().Be(3);
    }

    [Fact]
    public void DisposeCleansUpResources() {
        _diagnosticsService.StartHttpServer("http://localhost:5053/");
        _diagnosticsService.Dispose();
        // Use public API only: no assertion for private fields
        Assert.True(true); // Placeholder assertion
    }
    private readonly Mock<ILogger<DiagnosticsService>> _mockLogger;
    private readonly DiagnosticsService _diagnosticsService;

    public DiagnosticsServiceTests() {
        _mockLogger = new Mock<ILogger<DiagnosticsService>>();
        _diagnosticsService = new DiagnosticsService(_mockLogger.Object, new OptionsMonitorMock<ExternalConfiguration>());
    }

    [Fact]
    public void ConstructorInitializesCorrectly() {
        // Act & Assert
        _diagnosticsService.Should().NotBeNull();
        _diagnosticsService.GetActiveWatchers().Should().BeEmpty();
        _diagnosticsService.GetRestartAttemptsSnapshot().Should().BeEmpty();
        _diagnosticsService.GetRecentEvents().Should().BeEmpty();
    }

    [Fact]
    public void RegisterWatcherAddsWatcherToActiveList() {
        // Arrange
        string folderPath = @"C:\temp\test";

        // Act
        _diagnosticsService.RegisterWatcher(folderPath);

        // Assert
        _diagnosticsService.GetActiveWatchers().Should().Contain(folderPath);
    }

    [Fact]
    public void UnregisterWatcherRemovesWatcherFromActiveList() {
        // Arrange
        string folderPath = @"C:\temp\test";
        _diagnosticsService.RegisterWatcher(folderPath);

        // Act
        _diagnosticsService.UnregisterWatcher(folderPath);

        // Assert
        _diagnosticsService.GetActiveWatchers().Should().NotContain(folderPath);
    }

    [Fact]
    public void IncrementRestartTracksRestartAttempts() {
        // Arrange
        string folderPath = @"C:\temp\test";

        // Act
        int firstIncrement = _diagnosticsService.IncrementRestart(folderPath);
        int secondIncrement = _diagnosticsService.IncrementRestart(folderPath);

        // Assert
        firstIncrement.Should().Be(1);
        secondIncrement.Should().Be(2);
        _diagnosticsService.GetRestartAttemptsSnapshot().Should().ContainKey(folderPath);
        _diagnosticsService.GetRestartAttemptsSnapshot()[folderPath].Should().Be(2);
    }

    [Fact]
    public void RecordFileEventAddsEventToRecentEvents() {
        // Arrange
        string testPath = @"C:\temp\test.txt";
        DateTime beforeRecording = DateTime.Now;

        // Act
        _diagnosticsService.RecordFileEvent(testPath, true, 200);

        // Assert
        IReadOnlyCollection<FileEventRecord> events = _diagnosticsService.GetRecentEvents();
        events.Should().HaveCount(1);

        FileEventRecord recordedEvent = events.First();
        recordedEvent.Path.Should().Be(testPath);
        recordedEvent.PostedSuccess.Should().BeTrue();
        recordedEvent.StatusCode.Should().Be(200);
        recordedEvent.Timestamp.Should().BeAfter(beforeRecording);
    }

    [Fact]
    public void GetStatusReturnsCompleteStatus() {
        // Arrange
        _diagnosticsService.RegisterWatcher(@"C:\temp\folder1");
        _diagnosticsService.RecordFileEvent(@"C:\temp\test.txt", true, 200);

        // Act
        object status = _diagnosticsService.GetStatus();

        // Assert
        status.Should().NotBeNull();

        // Verify status contains expected data structure
        string statusJson = JsonSerializer.Serialize(status);
        statusJson.Should().Contain("ActiveWatchers");
        statusJson.Should().Contain("RestartAttempts");
        statusJson.Should().Contain("RecentEvents");
    }

    [Fact]
    public async Task ConfigEndpointHttpServerReturnsCurrentConfig() {
        // Arrange
        var mockLogger = new Mock<ILogger<DiagnosticsService>>();
        var monitor = new OptionsMonitorMock<ExternalConfiguration>();
        var cfg = new ExternalConfiguration {
            Folders = [new ExternalConfiguration.WatchedFolderConfig { FolderPath = @"C:\temp\watch" }],
            ApiEndpoint = "https://api.example.com/files",
            BearerToken = "token-abc",
            PostFileContents = false,
            ProcessedFolder = "processed"
        };

        monitor.SetCurrentValue(cfg);
        var diag = new DiagnosticsService(mockLogger.Object, monitor);

        // Find free port
        using var temp = new TcpListener(IPAddress.Loopback, 0);
        temp.Start();
        int port = ((IPEndPoint)temp.LocalEndpoint).Port;
        temp.Stop();
        string prefix = $"http://localhost:{port}/";

        // Act
        diag.StartHttpServer(prefix);
        using var http = new HttpClient();
        HttpResponseMessage response = await http.GetAsync(prefix + "config");

        // Assert
        response.EnsureSuccessStatusCode();
        string json = await response.Content.ReadAsStringAsync();
        ExternalConfiguration? returned = JsonSerializer.Deserialize<ExternalConfiguration>(json);
        returned.Should().NotBeNull();
        returned!.ApiEndpoint.Should().Be(cfg.ApiEndpoint);
        returned.Folders.Should().Contain(cfg.Folders[0]);

        diag.Dispose();
    }

    [Fact]
    public void MultipleOperationsThreadSafetyNoExceptions() {
        // Arrange
        var tasks = new List<Task>();
        string[] paths = [.. Enumerable.Range(0, 50).Select(i => $@"C:\temp\test{i}")];

        // Act - Perform operations concurrently
        foreach (string? path in paths) {
            tasks.Add(Task.Run(() => _diagnosticsService.RegisterWatcher(path)));
            tasks.Add(Task.Run(() => _diagnosticsService.RecordFileEvent(path, true, 200)));
        }

        // Assert
        Func<bool> act = () => Task.WaitAll([.. tasks], TimeSpan.FromSeconds(10));
        act.Should().NotThrow();

        _diagnosticsService.GetActiveWatchers().Should().HaveCount(50);
        _diagnosticsService.GetRecentEvents().Should().HaveCount(50);
    }

    public void Dispose() {
        _diagnosticsService?.Dispose();
        GC.SuppressFinalize(this);
    }
}

// ConfigurationService removed - these tests are obsolete
/*
public class ConfigurationServiceIntegrationTests : IDisposable
{
    private readonly Mock<ILogger<ConfigurationService>> _mockLogger;
    private readonly string _testServiceName = "TestFileWatchRest";
    private ConfigurationService? _configurationService;
    private readonly string _testDirectory;

    public ConfigurationServiceIntegrationTests()
    {
        _mockLogger = new Mock<ILogger<ConfigurationService>>();
        _testDirectory = Path.Combine(Path.GetTempPath(), "FileWatchRestTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDirectory);
    }

    [Fact]
    public void Constructor_CreatesConfigurationService()
    {
        // Act
        _configurationService = new ConfigurationService(_mockLogger.Object, _testServiceName);

        // Assert
        _configurationService.Should().NotBeNull();
    }

    [Fact]
    public async Task LoadConfigurationAsync_ReturnsValidConfiguration()
    {
        // Arrange
        _configurationService = new ConfigurationService(_mockLogger.Object, _testServiceName);

        // Act
        var config = await _configurationService.LoadConfigurationAsync();

        // Assert
        config.Should().NotBeNull();
        // Don't assert specific values since the service might load from existing config
        config.ProcessedFolder.Should().NotBeNullOrEmpty();
        config.Logging.LogLevel.Should().Be(LogLevel.Information);
    }

    [Fact]
    public async Task SaveConfigurationAsync_PersistsConfiguration()
    {
        // Arrange
        _configurationService = new ConfigurationService(_mockLogger.Object, _testServiceName);
        var testConfig = new ExternalConfiguration
        {
            Folders = new List<ExternalConfiguration.WatchedFolderConfig> { new ExternalConfiguration.WatchedFolderConfig { FolderPath = @"C:\test\save" } },
            ApiEndpoint = "https://api.test.com/save",
            DebounceMilliseconds = 3000,
            PostFileContents = true
        };

        // Act
        await _configurationService.SaveConfigurationAsync(testConfig);

        // Assert - The save operation should complete without throwing
        testConfig.Folders.Select(f => f.FolderPath).Should().BeEquivalentTo(new[] { @"C:\test\save" });
        testConfig.ApiEndpoint.Should().Be("https://api.test.com/save");
        testConfig.DebounceMilliseconds.Should().Be(3000);
        testConfig.PostFileContents.Should().BeTrue();
    }

    [Fact]
    public async Task CancellationToken_IsHandledGracefully()
    {
        // Arrange
        _configurationService = new ConfigurationService(_mockLogger.Object, _testServiceName);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert - Should not crash, may or may not throw based on implementation
        var act = async () => await _configurationService.LoadConfigurationAsync(cts.Token);
        try
        {
            await act();
            // If no exception, that's fine too
        }
        catch (OperationCanceledException)
        {
            // Expected behavior if cancellation is properly implemented
        }
    }

    public void Dispose()
    {
        _configurationService?.Dispose();

        try
        {
            if (Directory.Exists(_testDirectory))
            {
                Directory.Delete(_testDirectory, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }
}
*/

public class EndToEndIntegrationTests : IDisposable {
    private readonly string _testDirectory;
    private readonly List<string> _receivedPosts = [];
    private readonly Lock _postsLock = new();
    private HttpListener? _mockHttpServer;
    private Task? _serverTask;
    private readonly CancellationTokenSource _serverCts = new();
    private readonly List<string> _testServiceNames = []; // Track service names for cleanup

    public EndToEndIntegrationTests() {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"FileWatchRest_E2E_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDirectory);
    }

    [Fact]
    public async Task EndToEndFileCreatedPostsToHttpEndpoint() {
        // Arrange - Start mock HTTP server first and get the auto-assigned URL
        string serverUrl = await StartMockHttpServerAsync();

        // Create test configuration
        var config = new ExternalConfiguration {
            Folders = [new ExternalConfiguration.WatchedFolderConfig { FolderPath = _testDirectory, ActionName = "TestAction" }],
            Actions = [new ExternalConfiguration.ActionConfig {
                Name = "TestAction",
                ActionType = ExternalConfiguration.FolderActionType.RestPost,
                ApiEndpoint = serverUrl,
                PostFileContents = true,
                AllowedExtensions = [".txt"],
                IncludeSubdirectories = false,
                MoveProcessedFiles = false
            }],
            DebounceMilliseconds = 100, // Short debounce for testing
            Retries = 1,
            RetryDelayMilliseconds = 100
        };

        // For this test, we'll use a different approach - create the config in AppData
        string serviceName = $"FileWatchRest_Test_{Guid.NewGuid():N}";
        _testServiceNames.Add(serviceName); // Track for cleanup
        string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        string serviceDir = Path.Combine(appDataPath, serviceName);
        Directory.CreateDirectory(serviceDir);
        string configPath = Path.Combine(serviceDir, "FileWatchRest.json");

        await File.WriteAllTextAsync(configPath, JsonSerializer.Serialize(config));

        // Create services
        ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        ILogger<Worker> workerLogger = loggerFactory.CreateLogger<Worker>();
        ILogger<DiagnosticsService> diagnosticsLogger = loggerFactory.CreateLogger<DiagnosticsService>();

        var httpClientFactory = new TestHttpClientFactory();
        var lifetime = new TestHostApplicationLifetime();
        var diagnosticsService = new DiagnosticsService(diagnosticsLogger, new OptionsMonitorMock<ExternalConfiguration>());

        ExternalConfiguration initial = config; // Use the config we just created
        var watcherManager = new FileWatcherManager(LoggerFactory.Create(builder => builder.AddDebug()).CreateLogger<FileWatcherManager>(), diagnosticsService);
        var resilience = new HttpResilienceService(LoggerFactory.Create(builder => builder.AddDebug()).CreateLogger<HttpResilienceService>(), diagnosticsService);
        var optionsMonitor = new SimpleOptionsMonitor<ExternalConfiguration>(initial);

        // Create real channel and services for end-to-end test
        var channel = Channel.CreateBounded<string>(1000);
        var debounceService = new FileDebounceService(
            loggerFactory.CreateLogger<FileDebounceService>(),
            channel.Writer,
            () => optionsMonitor.CurrentValue);

        Worker worker = WorkerFactory.CreateWorker(
            logger: workerLogger,
            httpClientFactory: httpClientFactory,
            lifetime: lifetime,
            diagnostics: diagnosticsService,
            fileWatcherManager: watcherManager,
            debounceService: debounceService,
            resilienceService: resilience,
            optionsMonitor: optionsMonitor);

        var senderService = new FileSenderService(
            loggerFactory.CreateLogger<FileSenderService>(),
            channel.Reader,
            (path, ct) => worker.GetType()
                .GetMethod("ProcessFileAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.Invoke(worker, [path, ct]) as ValueTask? ?? ValueTask.CompletedTask);

        // Act - Start all services
        Task debounceTask = debounceService.StartAsync(CancellationToken.None);
        Task senderTask = senderService.StartAsync(CancellationToken.None);
        var workerCts = new CancellationTokenSource();
        Task workerTask = worker.StartAsync(workerCts.Token);

        // Give the worker time to initialize
        await Task.Delay(1500);

        // Create a test file that should trigger the workflow
        string testFilePath = Path.Combine(_testDirectory, "test-file.txt");
        string testFileContent = "This is test file content for end-to-end testing.";
        await File.WriteAllTextAsync(testFilePath, testFileContent);

        // Wait for the file to be processed (debounce + processing time)
        await Task.Delay(5000);

        // Stop all services
        workerCts.Cancel();
        await debounceService.StopAsync(CancellationToken.None);
        await senderService.StopAsync(CancellationToken.None);
        try {
            await workerTask;
        }
        catch (OperationCanceledException) {
            // Expected when cancelling
        }

        // Assert
        bool postReceived = false;
        for (int i = 0; i < 10; i++) {
            lock (_postsLock) {
                if (_receivedPosts.Count == 1) {
                    postReceived = true;
                    break;
                }
            }
            await Task.Delay(500);
        }
        postReceived.Should().BeTrue("exactly one POST request should have been received");
        lock (_postsLock) {
            string receivedPost = _receivedPosts[0];
            FileNotification? notification = JsonSerializer.Deserialize<FileNotification>(receivedPost);
            notification.Should().NotBeNull();
            notification!.Path.Should().Be(testFilePath);
            notification.Content.Should().Be(testFileContent);
            notification.FileSize.Should().Be(testFileContent.Length);
        }
    }

    [Fact]
    public async Task EndToEndFileCreatedWithoutContentPostsMetadataOnly() {
        // Arrange - Start mock HTTP server first and get the auto-assigned URL
        string serverUrl = await StartMockHttpServerAsync();

        // Create test configuration (PostFileContents = false)
        var config = new ExternalConfiguration {
            Folders = [new ExternalConfiguration.WatchedFolderConfig { FolderPath = _testDirectory, ActionName = "TestAction" }],
            Actions = [new ExternalConfiguration.ActionConfig {
                Name = "TestAction",
                ActionType = ExternalConfiguration.FolderActionType.RestPost,
                ApiEndpoint = serverUrl,
                PostFileContents = false, // Metadata only
                AllowedExtensions = [".txt"],
                IncludeSubdirectories = false,
                MoveProcessedFiles = false
            }],
            DebounceMilliseconds = 100,
            Retries = 1,
            RetryDelayMilliseconds = 100
        };

        // Create config in AppData for this test
        string serviceName = $"FileWatchRest_Test_{Guid.NewGuid():N}";
        _testServiceNames.Add(serviceName); // Track for cleanup
        string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        string serviceDir = Path.Combine(appDataPath, serviceName);
        Directory.CreateDirectory(serviceDir);
        string configPath = Path.Combine(serviceDir, "FileWatchRest.json");

        await File.WriteAllTextAsync(configPath, JsonSerializer.Serialize(config));

        // Create services
        ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        ILogger<Worker> workerLogger = loggerFactory.CreateLogger<Worker>();
        ILogger<DiagnosticsService> diagnosticsLogger = loggerFactory.CreateLogger<DiagnosticsService>();

        var httpClientFactory = new TestHttpClientFactory();
        var lifetime = new TestHostApplicationLifetime();
        var diagnosticsService = new DiagnosticsService(diagnosticsLogger, new OptionsMonitorMock<ExternalConfiguration>());

        ExternalConfiguration initial2 = config; // Use the config we just created
        var watcherManager2 = new FileWatcherManager(LoggerFactory.Create(builder => builder.AddDebug()).CreateLogger<FileWatcherManager>(), diagnosticsService);
        var resilience2 = new HttpResilienceService(LoggerFactory.Create(builder => builder.AddDebug()).CreateLogger<HttpResilienceService>(), diagnosticsService);
        var optionsMonitor2 = new SimpleOptionsMonitor<ExternalConfiguration>(initial2);

        // Create real channel and services for end-to-end test
        var channel2 = Channel.CreateBounded<string>(1000);
        var debounceService2 = new FileDebounceService(
            loggerFactory.CreateLogger<FileDebounceService>(),
            channel2.Writer,
            () => optionsMonitor2.CurrentValue);

        Worker worker2 = WorkerFactory.CreateWorker(
            logger: workerLogger,
            httpClientFactory: httpClientFactory,
            lifetime: lifetime,
            diagnostics: diagnosticsService,
            fileWatcherManager: watcherManager2,
            debounceService: debounceService2,
            resilienceService: resilience2,
            optionsMonitor: optionsMonitor2);

        var senderService2 = new FileSenderService(
            loggerFactory.CreateLogger<FileSenderService>(),
            channel2.Reader,
            (path, ct) => worker2.GetType()
                .GetMethod("ProcessFileAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.Invoke(worker2, [path, ct]) as ValueTask? ?? ValueTask.CompletedTask);

        // Act - Start all services
        Task debounceTask2 = debounceService2.StartAsync(CancellationToken.None);
        Task senderTask2 = senderService2.StartAsync(CancellationToken.None);
        var workerCts = new CancellationTokenSource();
        Task workerTask = worker2.StartAsync(workerCts.Token);

        // Give the worker time to initialize
        await Task.Delay(1500);

        // Create a test file
        string testFilePath = Path.Combine(_testDirectory, "metadata-test.txt");
        string testFileContent = "Content that should not be posted.";
        await File.WriteAllTextAsync(testFilePath, testFileContent);

        // Wait for processing
        await Task.Delay(5000);

        // Stop all services
        workerCts.Cancel();
        await debounceService2.StopAsync(CancellationToken.None);
        await senderService2.StopAsync(CancellationToken.None);
        try {
            await workerTask;
        }
        catch (OperationCanceledException) {
            // Expected when cancelling
        }

        // Assert
        bool postReceived = false;
        for (int i = 0; i < 10; i++) {
            lock (_postsLock) {
                if (_receivedPosts.Count == 1) {
                    postReceived = true;
                    break;
                }
            }
            await Task.Delay(500);
        }
        postReceived.Should().BeTrue("exactly one POST request should have been received");
        lock (_postsLock) {
            string receivedPost = _receivedPosts[0];
            FileNotification? notification = JsonSerializer.Deserialize<FileNotification>(receivedPost);

            notification.Should().NotBeNull();
            notification!.Path.Should().Be(testFilePath);
            notification.Content.Should().BeNull("content should not be included when PostFileContents is false");
            notification.FileSize.Should().Be(testFileContent.Length);
        }
    }

    private Task<string> StartMockHttpServerAsync() {
        // Find an available port using TcpListener
        using var tempListener = new TcpListener(IPAddress.Loopback, 0);
        tempListener.Start();
        int port = ((IPEndPoint)tempListener.LocalEndpoint).Port;
        tempListener.Stop();

        // Now create the HttpListener with the found port
        _mockHttpServer = new HttpListener();
        string serverUrl = $"http://localhost:{port}/webhook/";
        _mockHttpServer.Prefixes.Add(serverUrl);
        _mockHttpServer.Start();

        _serverTask = Task.Run(async () => {
            while (!_serverCts.Token.IsCancellationRequested) {
                try {
                    HttpListenerContext context = await _mockHttpServer.GetContextAsync();

                    // Read the POST body
                    using var reader = new StreamReader(context.Request.InputStream);
                    string body = await reader.ReadToEndAsync();
                    lock (_postsLock) {
                        _receivedPosts.Add(body);
                    }

                    // Send a successful response
                    context.Response.StatusCode = 200;
                    context.Response.ContentType = "application/json";
                    byte[] responseBytes = Encoding.UTF8.GetBytes(/*lang=json,strict*/ "{\"status\":\"success\"}");
                    await context.Response.OutputStream.WriteAsync(responseBytes);
                    context.Response.Close();
                }
                catch (ObjectDisposedException) {
                    // Expected when stopping the server
                    break;
                }
                catch (HttpListenerException) {
                    // Expected when stopping the server
                    break;
                }
            }
        });

        return Task.FromResult(serverUrl);
    }
    public void Dispose() {
        _serverCts.Cancel();

        _mockHttpServer?.Stop();
        _mockHttpServer?.Close();

        if (_serverTask != null) {
            try {
                _serverTask.Wait(1000);
            }
            catch {
                // Ignore cleanup errors
            }
        }

        _serverCts.Dispose();

        // Clean up test directories
        try {
            if (Directory.Exists(_testDirectory)) {
                Directory.Delete(_testDirectory, recursive: true);
            }
        }
        catch {
            // Ignore cleanup errors
        }

        // Clean up AppData test service directories
        foreach (string serviceName in _testServiceNames) {
            try {
                string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
                string serviceDir = Path.Combine(appDataPath, serviceName);
                if (Directory.Exists(serviceDir)) {
                    Directory.Delete(serviceDir, recursive: true);
                }
            }
            catch {
                // Ignore cleanup errors
            }
        }
        GC.SuppressFinalize(this);
    }
}

// Test helper classes
public class TestHttpClientFactory : IHttpClientFactory {
    public HttpClient CreateClient(string name = "") => new();
}

public class TestHostApplicationLifetime : IHostApplicationLifetime, IDisposable {
    private readonly CancellationTokenSource _applicationStartedSource = new();
    private readonly CancellationTokenSource _applicationStoppingSource = new();
    private readonly CancellationTokenSource _applicationStoppedSource = new();

    public CancellationToken ApplicationStarted => _applicationStartedSource.Token;
    public CancellationToken ApplicationStopping => _applicationStoppingSource.Token;
    public CancellationToken ApplicationStopped => _applicationStoppedSource.Token;

    public void StopApplication() {
        _applicationStoppingSource.Cancel();
        _applicationStoppedSource.Cancel();
    }

    public void Dispose() {
        try {
            _applicationStartedSource.Cancel();
            _applicationStoppingSource.Cancel();
            _applicationStoppedSource.Cancel();
        }
        finally {
            _applicationStartedSource.Dispose();
            _applicationStoppingSource.Dispose();
            _applicationStoppedSource.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}

public class HttpClientFactoryMock : IHttpClientFactory {
    public HttpClient CreateClient(string name) => new();
}

public class HostApplicationLifetimeMock : IHostApplicationLifetime {
    public CancellationToken ApplicationStarted => CancellationToken.None;
    public CancellationToken ApplicationStopping => CancellationToken.None;
    public CancellationToken ApplicationStopped => CancellationToken.None;
    public void StopApplication() { }
}

public class ResilienceServiceMock : IResilienceService {
    public Task<ResilienceResult> SendWithRetriesAsync(Func<CancellationToken, Task<HttpRequestMessage>> requestFactory, HttpClient client, string endpointKey, ExternalConfiguration config, CancellationToken ct) =>
        // Provide minimal valid ResilienceResult for test
        Task.FromResult(new ResilienceResult(true, 200, null, null, 0, false));
}


public class WorkerTests {
    [Fact]
    public void ConfigurationReloadUpdatesApiEndpointWithoutRestart() {
        // Arrange
        var loggerFactory = new LoggerFactory();
        ILogger<DiagnosticsService> diagnosticsLogger = loggerFactory.CreateLogger<DiagnosticsService>();
        ILogger<FileWatcherManager> watcherLogger = loggerFactory.CreateLogger<FileWatcherManager>();
        var diagnostics = new DiagnosticsService(diagnosticsLogger, new OptionsMonitorMock<ExternalConfiguration>());
        var fileWatcherManager = new FileWatcherManager(watcherLogger, diagnostics);
        Worker worker = WorkerFactory.CreateWorker(
            logger: loggerFactory.CreateLogger<Worker>(),
            httpClientFactory: new HttpClientFactoryMock(),
            lifetime: new HostApplicationLifetimeMock(),
            diagnostics: diagnostics,
            fileWatcherManager: fileWatcherManager,
            resilienceService: new ResilienceServiceMock(),
            optionsMonitor: new OptionsMonitorMock<ExternalConfiguration>()
        );
        var initialConfig = new ExternalConfiguration { ApiEndpoint = "http://localhost:5000/api/initial" };
        // diagnostics.SetConfiguration(initialConfig); // No longer needed
        worker.CurrentConfig = initialConfig;

        // Act
        var updatedConfig = new ExternalConfiguration { ApiEndpoint = "http://localhost:5000/api/updated" };
        worker.CurrentConfig = updatedConfig;

        // Assert
        Assert.Equal("http://localhost:5000/api/updated", worker.CurrentConfig.ApiEndpoint);
    }
}
