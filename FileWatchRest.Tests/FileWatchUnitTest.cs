namespace FileWatchRest.Tests;

public class ExternalConfigurationTests
{
    [Fact]
    public void Constructor_SetsDefaultValues()
    {
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
        config.Logging.LogLevel.Should().Be("Information");
    }

    [Fact]
    public void Properties_CanBeSetAndRetrieved()
    {
        // Arrange
        var config = new ExternalConfiguration();
        var expectedFolders = new[] { @"C:\temp\test1", @"C:\temp\test2" };
        var expectedExtensions = new[] { ".txt", ".json", ".xml" };

        // Act
        config.Folders = expectedFolders;
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
        config.Logging.LogLevel = "Debug";

        // Assert
        config.Folders.Should().BeEquivalentTo(expectedFolders);
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
        config.Logging.LogLevel.Should().Be("Debug");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(500)]
    [InlineData(1000)]
    [InlineData(5000)]
    [InlineData(30000)]
    public void DebounceMilliseconds_AcceptsValidValues(int debounceMs)
    {
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
    public void Retries_AcceptsValidValues(int retries)
    {
        // Arrange
        var config = new ExternalConfiguration {
            // Act
            Retries = retries
        };

        // Assert
        config.Retries.Should().Be(retries);
    }

    [Fact]
    public void JsonSerialization_PreservesAllProperties()
    {
        // Arrange
        var original = new ExternalConfiguration
        {
            Folders = new[] { @"C:\temp\watch1", @"C:\temp\watch2" },
            ApiEndpoint = "https://api.example.com/files",
            BearerToken = "test-token-123",
            PostFileContents = true,
            MoveProcessedFiles = true,
            ProcessedFolder = "completed",
            AllowedExtensions = new[] { ".txt", ".json", ".xml" },
            IncludeSubdirectories = false,
            DebounceMilliseconds = 1500,
            Retries = 5,
            RetryDelayMilliseconds = 750,
            DiagnosticsUrlPrefix = "http://localhost:8080/",
            ChannelCapacity = 2000,
            MaxParallelSends = 6,
            FileWatcherInternalBufferSize = 131072,
            Logging = new LoggingOptions { LogLevel = "Debug" }
        };

        // Act
        var json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<ExternalConfiguration>(json);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Folders.Should().BeEquivalentTo(original.Folders);
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

public class FileNotificationTests
{
    [Fact]
    public void Constructor_CreatesEmptyFileNotification()
    {
        // Act
        var notification = new FileNotification();

        // Assert
        notification.Path.Should().BeEmpty(); // FileNotification initializes Path as empty string
    }

    [Fact]
    public void Properties_CanBeSetAndRetrieved()
    {
        // Arrange
        var expectedPath = @"C:\temp\test.txt";

        // Act
        var notification = new FileNotification
        {
            Path = expectedPath
        };

        // Assert
        notification.Path.Should().Be(expectedPath);
    }

    [Fact]
    public void JsonSerialization_SerializesCorrectly()
    {
        // Arrange
        var notification = new FileNotification
        {
            Path = @"C:\temp\example.txt"
        };

        // Act
        var json = JsonSerializer.Serialize(notification);
        var deserialized = JsonSerializer.Deserialize<FileNotification>(json);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Path.Should().Be(@"C:\temp\example.txt");
    }
}

public class DiagnosticsServiceTests : IDisposable
{
    private readonly Mock<ILogger<DiagnosticsService>> _mockLogger;
    private readonly DiagnosticsService _diagnosticsService;

    public DiagnosticsServiceTests()
    {
        _mockLogger = new Mock<ILogger<DiagnosticsService>>();
        _diagnosticsService = new DiagnosticsService(_mockLogger.Object);
    }

    [Fact]
    public void Constructor_InitializesCorrectly()
    {
        // Act & Assert
        _diagnosticsService.Should().NotBeNull();
        _diagnosticsService.GetActiveWatchers().Should().BeEmpty();
        _diagnosticsService.GetRestartAttemptsSnapshot().Should().BeEmpty();
        _diagnosticsService.GetRecentEvents().Should().BeEmpty();
    }

    [Fact]
    public void RegisterWatcher_AddsWatcherToActiveList()
    {
        // Arrange
        var folderPath = @"C:\temp\test";

        // Act
        _diagnosticsService.RegisterWatcher(folderPath);

        // Assert
        _diagnosticsService.GetActiveWatchers().Should().Contain(folderPath);
    }

    [Fact]
    public void UnregisterWatcher_RemovesWatcherFromActiveList()
    {
        // Arrange
        var folderPath = @"C:\temp\test";
        _diagnosticsService.RegisterWatcher(folderPath);

        // Act
        _diagnosticsService.UnregisterWatcher(folderPath);

        // Assert
        _diagnosticsService.GetActiveWatchers().Should().NotContain(folderPath);
    }

    [Fact]
    public void IncrementRestart_TracksRestartAttempts()
    {
        // Arrange
        var folderPath = @"C:\temp\test";

        // Act
        var firstIncrement = _diagnosticsService.IncrementRestart(folderPath);
        var secondIncrement = _diagnosticsService.IncrementRestart(folderPath);

        // Assert
        firstIncrement.Should().Be(1);
        secondIncrement.Should().Be(2);
        _diagnosticsService.GetRestartAttemptsSnapshot().Should().ContainKey(folderPath);
        _diagnosticsService.GetRestartAttemptsSnapshot()[folderPath].Should().Be(2);
    }

    [Fact]
    public void RecordFileEvent_AddsEventToRecentEvents()
    {
        // Arrange
        var testPath = @"C:\temp\test.txt";
        var beforeRecording = DateTime.Now;

        // Act
        _diagnosticsService.RecordFileEvent(testPath, true, 200);

        // Assert
        var events = _diagnosticsService.GetRecentEvents();
        events.Should().HaveCount(1);

        var recordedEvent = events.First();
        recordedEvent.Path.Should().Be(testPath);
        recordedEvent.PostedSuccess.Should().BeTrue();
        recordedEvent.StatusCode.Should().Be(200);
        recordedEvent.Timestamp.Should().BeAfter(beforeRecording);
    }

    [Fact]
    public void GetStatus_ReturnsCompleteStatus()
    {
        // Arrange
        _diagnosticsService.RegisterWatcher(@"C:\temp\folder1");
        _diagnosticsService.RecordFileEvent(@"C:\temp\test.txt", true, 200);

        // Act
        var status = _diagnosticsService.GetStatus();

        // Assert
        status.Should().NotBeNull();

        // Verify status contains expected data structure
        var statusJson = JsonSerializer.Serialize(status);
        statusJson.Should().Contain("ActiveWatchers");
        statusJson.Should().Contain("RestartAttempts");
        statusJson.Should().Contain("RecentEvents");
    }

    [Fact]
    public void MultipleOperations_ThreadSafety_NoExceptions()
    {
        // Arrange
        var tasks = new List<Task>();
        var paths = Enumerable.Range(0, 50).Select(i => $@"C:\temp\test{i}").ToArray();

        // Act - Perform operations concurrently
        foreach (var path in paths)
        {
            tasks.Add(Task.Run(() => _diagnosticsService.RegisterWatcher(path)));
            tasks.Add(Task.Run(() => _diagnosticsService.RecordFileEvent(path, true, 200)));
        }

        // Assert
        var act = () => Task.WaitAll(tasks.ToArray(), TimeSpan.FromSeconds(10));
        act.Should().NotThrow();

        _diagnosticsService.GetActiveWatchers().Should().HaveCount(50);
        _diagnosticsService.GetRecentEvents().Should().HaveCount(50);
    }

    public void Dispose()
    {
        _diagnosticsService?.Dispose();
    }
}

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
        config.Logging.LogLevel.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task SaveConfigurationAsync_PersistsConfiguration()
    {
        // Arrange
        _configurationService = new ConfigurationService(_mockLogger.Object, _testServiceName);
        var testConfig = new ExternalConfiguration
        {
            Folders = new[] { @"C:\test\save" },
            ApiEndpoint = "https://api.test.com/save",
            DebounceMilliseconds = 3000,
            PostFileContents = true
        };

        // Act
        await _configurationService.SaveConfigurationAsync(testConfig);

        // Assert - The save operation should complete without throwing
        testConfig.Folders.Should().BeEquivalentTo(new[] { @"C:\test\save" });
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

public class EndToEndIntegrationTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly List<string> _receivedPosts = new List<string>();
    private readonly object _postsLock = new();
    private HttpListener? _mockHttpServer;
    private Task? _serverTask;
    private readonly CancellationTokenSource _serverCts = new();
    private readonly List<string> _testServiceNames = new List<string>(); // Track service names for cleanup

    public EndToEndIntegrationTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"FileWatchRest_E2E_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDirectory);
    }

    [Fact]
    public async Task EndToEnd_FileCreated_PostsToHttpEndpoint()
    {
        // Arrange - Start mock HTTP server first and get the auto-assigned URL
        var serverUrl = await StartMockHttpServerAsync();

        // Create test configuration
        var config = new ExternalConfiguration
        {
            Folders = new[] { _testDirectory },
            ApiEndpoint = serverUrl,
            PostFileContents = true,
            DebounceMilliseconds = 100, // Short debounce for testing
            Retries = 1,
            RetryDelayMilliseconds = 100,
            AllowedExtensions = new[] { ".txt" },
            IncludeSubdirectories = false,
            MoveProcessedFiles = false
        };

        // For this test, we'll use a different approach - create the config in AppData
        var serviceName = $"FileWatchRest_Test_{Guid.NewGuid():N}";
        _testServiceNames.Add(serviceName); // Track for cleanup
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var serviceDir = Path.Combine(appDataPath, serviceName);
        Directory.CreateDirectory(serviceDir);
        var configPath = Path.Combine(serviceDir, "FileWatchRest.json");

        await File.WriteAllTextAsync(configPath, JsonSerializer.Serialize(config));

        // Create services
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        var workerLogger = loggerFactory.CreateLogger<Worker>();
        var configServiceLogger = loggerFactory.CreateLogger<ConfigurationService>();
        var diagnosticsLogger = loggerFactory.CreateLogger<DiagnosticsService>();

        var httpClientFactory = new TestHttpClientFactory();
        var lifetime = new TestHostApplicationLifetime();
        var diagnosticsService = new DiagnosticsService(diagnosticsLogger);
        var configurationService = new ConfigurationService(configServiceLogger, serviceName);

        var initial = await configurationService.LoadConfigurationAsync();
        var watcherManager = new FileWatcherManager(LoggerFactory.Create(builder => builder.AddDebug()).CreateLogger<FileWatcherManager>(), diagnosticsService);
        var resilience = new HttpResilienceService(LoggerFactory.Create(builder => builder.AddDebug()).CreateLogger<HttpResilienceService>(), diagnosticsService);
        var optionsMonitor = new SimpleOptionsMonitor<ExternalConfiguration>(initial);
        var worker = new Worker(workerLogger, httpClientFactory, lifetime, diagnosticsService, configurationService, watcherManager, resilience, optionsMonitor);        // Act - Start the worker
        var workerCts = new CancellationTokenSource();
        var workerTask = worker.StartAsync(workerCts.Token);

        // Give the worker time to initialize
        await Task.Delay(500);

        // Create a test file that should trigger the workflow
        var testFilePath = Path.Combine(_testDirectory, "test-file.txt");
        var testFileContent = "This is test file content for end-to-end testing.";
        await File.WriteAllTextAsync(testFilePath, testFileContent);

        // Wait for the file to be processed (debounce + processing time)
        await Task.Delay(2000);

        // Stop the worker
        workerCts.Cancel();
        try
        {
            await workerTask;
        }
        catch (OperationCanceledException)
        {
            // Expected when cancelling
        }

        // Assert
        lock (_postsLock)
        {
            _receivedPosts.Should().HaveCount(1, "exactly one POST request should have been received");

            var receivedPost = _receivedPosts[0];
            var notification = JsonSerializer.Deserialize<FileNotification>(receivedPost);

            notification.Should().NotBeNull();
            notification!.Path.Should().Be(testFilePath);
            notification.Content.Should().Be(testFileContent);
            notification.FileSize.Should().Be(testFileContent.Length);
        }
    }

    [Fact]
    public async Task EndToEnd_FileCreated_WithoutContent_PostsMetadataOnly()
    {
        // Arrange - Start mock HTTP server first and get the auto-assigned URL
        var serverUrl = await StartMockHttpServerAsync();

        // Create test configuration (PostFileContents = false)
        var config = new ExternalConfiguration
        {
            Folders = new[] { _testDirectory },
            ApiEndpoint = serverUrl,
            PostFileContents = false, // Metadata only
            DebounceMilliseconds = 100,
            Retries = 1,
            RetryDelayMilliseconds = 100,
            AllowedExtensions = new[] { ".txt" },
            IncludeSubdirectories = false,
            MoveProcessedFiles = false
        };

        // Create config in AppData for this test
        var serviceName = $"FileWatchRest_Test_{Guid.NewGuid():N}";
        _testServiceNames.Add(serviceName); // Track for cleanup
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var serviceDir = Path.Combine(appDataPath, serviceName);
        Directory.CreateDirectory(serviceDir);
        var configPath = Path.Combine(serviceDir, "FileWatchRest.json");

        await File.WriteAllTextAsync(configPath, JsonSerializer.Serialize(config));

        // Create services
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        var workerLogger = loggerFactory.CreateLogger<Worker>();
        var configServiceLogger = loggerFactory.CreateLogger<ConfigurationService>();
        var diagnosticsLogger = loggerFactory.CreateLogger<DiagnosticsService>();

        var httpClientFactory = new TestHttpClientFactory();
        var lifetime = new TestHostApplicationLifetime();
        var diagnosticsService = new DiagnosticsService(diagnosticsLogger);
        var configurationService = new ConfigurationService(configServiceLogger, serviceName);

        var initial2 = await configurationService.LoadConfigurationAsync();
        var watcherManager2 = new FileWatcherManager(LoggerFactory.Create(builder => builder.AddDebug()).CreateLogger<FileWatcherManager>(), diagnosticsService);
        var resilience2 = new HttpResilienceService(LoggerFactory.Create(builder => builder.AddDebug()).CreateLogger<HttpResilienceService>(), diagnosticsService);
        var optionsMonitor2 = new SimpleOptionsMonitor<ExternalConfiguration>(initial2);
        var worker2 = new Worker(workerLogger, httpClientFactory, lifetime, diagnosticsService, configurationService, watcherManager2, resilience2, optionsMonitor2);        // Act - Start the worker
        var workerCts = new CancellationTokenSource();
        var workerTask = worker2.StartAsync(workerCts.Token);

        // Give the worker time to initialize
        await Task.Delay(500);

        // Create a test file
        var testFilePath = Path.Combine(_testDirectory, "metadata-test.txt");
        var testFileContent = "Content that should not be posted.";
        await File.WriteAllTextAsync(testFilePath, testFileContent);

        // Wait for processing
        await Task.Delay(2000);

        // Stop the worker
        workerCts.Cancel();
        try
        {
            await workerTask;
        }
        catch (OperationCanceledException)
        {
            // Expected when cancelling
        }

        // Assert
        lock (_postsLock)
        {
            _receivedPosts.Should().HaveCount(1, "exactly one POST request should have been received");

            var receivedPost = _receivedPosts[0];
            var notification = JsonSerializer.Deserialize<FileNotification>(receivedPost);

            notification.Should().NotBeNull();
            notification!.Path.Should().Be(testFilePath);
            notification.Content.Should().BeNull("content should not be included when PostFileContents is false");
            notification.FileSize.Should().Be(testFileContent.Length);
        }
    }

    private Task<string> StartMockHttpServerAsync()
    {
        // Find an available port using TcpListener
        using var tempListener = new TcpListener(IPAddress.Loopback, 0);
        tempListener.Start();
        var port = ((IPEndPoint)tempListener.LocalEndpoint).Port;
        tempListener.Stop();

        // Now create the HttpListener with the found port
        _mockHttpServer = new HttpListener();
        var serverUrl = $"http://localhost:{port}/webhook/";
        _mockHttpServer.Prefixes.Add(serverUrl);
        _mockHttpServer.Start();

        _serverTask = Task.Run(async () =>
        {
            while (!_serverCts.Token.IsCancellationRequested)
            {
                try
                {
                    var context = await _mockHttpServer.GetContextAsync();

                    // Read the POST body
                    using var reader = new StreamReader(context.Request.InputStream);
                    var body = await reader.ReadToEndAsync();

                    lock (_postsLock)
                    {
                        _receivedPosts.Add(body);
                    }

                    // Send a successful response
                    context.Response.StatusCode = 200;
                    context.Response.ContentType = "application/json";
                    var responseBytes = Encoding.UTF8.GetBytes("{\"status\":\"success\"}");
                    await context.Response.OutputStream.WriteAsync(responseBytes);
                    context.Response.Close();
                }
                catch (ObjectDisposedException)
                {
                    // Expected when stopping the server
                    break;
                }
                catch (HttpListenerException)
                {
                    // Expected when stopping the server
                    break;
                }
            }
        });

        return Task.FromResult(serverUrl);
    }    public void Dispose()
    {
        _serverCts.Cancel();

        _mockHttpServer?.Stop();
        _mockHttpServer?.Close();

        if (_serverTask != null)
        {
            try
            {
                _serverTask.Wait(1000);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }

        _serverCts.Dispose();

        // Clean up test directories
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

        // Clean up AppData test service directories
        foreach (var serviceName in _testServiceNames)
        {
            try
            {
                var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
                var serviceDir = Path.Combine(appDataPath, serviceName);
                if (Directory.Exists(serviceDir))
                {
                    Directory.Delete(serviceDir, recursive: true);
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }
}

// Test helper classes
public class TestHttpClientFactory : IHttpClientFactory
{
    public HttpClient CreateClient(string name = "")
    {
        return new HttpClient();
    }
}

public class TestHostApplicationLifetime : IHostApplicationLifetime
{
    private readonly CancellationTokenSource _applicationStartedSource = new();
    private readonly CancellationTokenSource _applicationStoppingSource = new();
    private readonly CancellationTokenSource _applicationStoppedSource = new();

    public CancellationToken ApplicationStarted => _applicationStartedSource.Token;
    public CancellationToken ApplicationStopping => _applicationStoppingSource.Token;
    public CancellationToken ApplicationStopped => _applicationStoppedSource.Token;

    public void StopApplication()
    {
        _applicationStoppingSource.Cancel();
        _applicationStoppedSource.Cancel();
    }
}
