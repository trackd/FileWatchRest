using FileWatchRest.Configuration;
using FileWatchRest.Services;
using Microsoft.Extensions.Logging;

namespace FileWatchRest.Tests;

/// <summary>
/// Tests for Worker.MoveToProcessedFolderAsync functionality
/// </summary>
public class WorkerMoveProcessedTests : IDisposable {
    private readonly string _testDirectory;

    public WorkerMoveProcessedTests() {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"FileWatchRest_MoveTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDirectory);
    }

    [Fact]
    public async Task MoveToProcessedFolderCreatesProcessedDirectory() {
        // Arrange
        ILoggerFactory loggerFactory = LoggerFactory.Create(b => b.AddDebug().SetMinimumLevel(LogLevel.Debug));
        string testFilePath = Path.Combine(_testDirectory, "test-file.txt");
        await File.WriteAllTextAsync(testFilePath, "test content");

        var config = new ExternalConfiguration {
            ProcessedFolder = "processed",
            MoveProcessedFiles = true,
            Folders = [new() { FolderPath = _testDirectory, ActionName = "TestAction" }],
            Actions = [new() {
                Name = "TestAction",
                ActionType = ExternalConfiguration.FolderActionType.RestPost,
                ApiEndpoint = "http://localhost:8080/api/files",
                ProcessedFolder = "processed",
                MoveProcessedFiles = true
            }]
        };

        var diagService = new DiagnosticsService(loggerFactory.CreateLogger<DiagnosticsService>(), new TestUtilities.OptionsMonitorMock<ExternalConfiguration>());

        Worker worker = WorkerFactory.CreateWorker(
            logger: loggerFactory.CreateLogger<Worker>(),
            httpClientFactory: new TestHttpClientFactory(),
            lifetime: new TestHostApplicationLifetime(),
            diagnostics: diagService,
            fileWatcherManager: new FileWatcherManager(loggerFactory.CreateLogger<FileWatcherManager>(), diagService),
            resilienceService: new HttpResilienceService(loggerFactory.CreateLogger<HttpResilienceService>(), diagService),
            optionsMonitor: new SimpleOptionsMonitor<ExternalConfiguration>(config)
        );

        worker.CurrentConfig = config;

        // Act
        System.Reflection.MethodInfo? moveMethod = typeof(Worker).GetMethod("MoveToProcessedFolderAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        object? result = moveMethod?.Invoke(worker, [testFilePath, config, CancellationToken.None]);
        if (result is Task task) {
            await task;
        }

        // Assert
        string processedDir = Path.Combine(_testDirectory, "processed");
        Assert.True(Directory.Exists(processedDir));
        Assert.False(File.Exists(testFilePath)); // Original file moved
        Assert.Single(Directory.GetFiles(processedDir)); // One file in processed
    }

    [Fact]
    public async Task MoveToProcessedFolderHandlesTimestampCollisions() {
        // Arrange
        ILoggerFactory loggerFactory = LoggerFactory.Create(b => b.AddDebug().SetMinimumLevel(LogLevel.Debug));
        string testFile1 = Path.Combine(_testDirectory, "test-collision1.txt");
        string testFile2 = Path.Combine(_testDirectory, "test-collision2.txt");
        await File.WriteAllTextAsync(testFile1, "content1");
        await File.WriteAllTextAsync(testFile2, "content2");

        var config = new ExternalConfiguration {
            ProcessedFolder = "processed",
            MoveProcessedFiles = true,
            ApiEndpoint = "http://localhost:8080/api/files",
            Folders =
            [
                new() { FolderPath = _testDirectory, ActionName = "TestAction" }
            ],
            Actions = [new() { Name = "TestAction", ActionType = ExternalConfiguration.FolderActionType.RestPost, ApiEndpoint = "http://localhost:8080/api/files" }]
        };

        var diagService = new DiagnosticsService(loggerFactory.CreateLogger<DiagnosticsService>(), new TestUtilities.OptionsMonitorMock<ExternalConfiguration>());

        Worker worker = WorkerFactory.CreateWorker(
            logger: loggerFactory.CreateLogger<Worker>(),
            httpClientFactory: new TestHttpClientFactory(),
            lifetime: new TestHostApplicationLifetime(),
            diagnostics: diagService,
            fileWatcherManager: new FileWatcherManager(loggerFactory.CreateLogger<FileWatcherManager>(), diagService),
            resilienceService: new HttpResilienceService(loggerFactory.CreateLogger<HttpResilienceService>(), diagService),
            optionsMonitor: new SimpleOptionsMonitor<ExternalConfiguration>(config)
        );

        worker.CurrentConfig = config;

        System.Reflection.MethodInfo? moveMethod = typeof(Worker).GetMethod("MoveToProcessedFolderAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        if (moveMethod?.Invoke(worker, [testFile1, config, CancellationToken.None]) is Task task1) {
            await task1;
        }

        if (moveMethod?.Invoke(worker, [testFile2, config, CancellationToken.None]) is Task task2) {
            await task2;
        }

        // Assert
        string processedDir = Path.Combine(_testDirectory, "processed");
        string[] processedFiles = Directory.GetFiles(processedDir);
        Assert.Equal(2, processedFiles.Length); // Both files moved with unique names
        Assert.False(File.Exists(testFile1));
        Assert.False(File.Exists(testFile2));
    }

    [Fact]
    public async Task MoveToProcessedFolderHandlesErrorsContinuesGracefully() {
        // Arrange
        ILoggerFactory loggerFactory = LoggerFactory.Create(b => b.AddDebug().SetMinimumLevel(LogLevel.Debug));
        string nonExistentFile = Path.Combine(_testDirectory, "does-not-exist.txt");

        var config = new ExternalConfiguration {
            ProcessedFolder = "processed",
            MoveProcessedFiles = true,
            ApiEndpoint = "http://localhost:8080/api/files",
            Folders =
            [
                new() { FolderPath = _testDirectory, ActionName = "TestAction" }
            ],
            Actions = [new() { Name = "TestAction", ActionType = ExternalConfiguration.FolderActionType.RestPost, ApiEndpoint = "http://localhost:8080/api/files" }]
        };

        var diagService = new DiagnosticsService(loggerFactory.CreateLogger<DiagnosticsService>(), new TestUtilities.OptionsMonitorMock<ExternalConfiguration>());

        Worker worker = WorkerFactory.CreateWorker(
            logger: loggerFactory.CreateLogger<Worker>(),
            httpClientFactory: new TestHttpClientFactory(),
            lifetime: new TestHostApplicationLifetime(),
            diagnostics: diagService,
            fileWatcherManager: new FileWatcherManager(loggerFactory.CreateLogger<FileWatcherManager>(), diagService),
            resilienceService: new HttpResilienceService(loggerFactory.CreateLogger<HttpResilienceService>(), diagService),
            optionsMonitor: new SimpleOptionsMonitor<ExternalConfiguration>(config)
        );

        worker.CurrentConfig = config;

        System.Reflection.MethodInfo? moveMethod = typeof(Worker).GetMethod("MoveToProcessedFolderAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        // Act - Should not throw
        if (moveMethod?.Invoke(worker, [nonExistentFile, config, CancellationToken.None]) is Task task) {
            await task; // Should complete without exception
        }

        // Assert - No exception thrown (error logged internally)
        Assert.True(true); // Test passes if no exception
    }

    public void Dispose() {
        try {
            if (Directory.Exists(_testDirectory)) {
                Directory.Delete(_testDirectory, recursive: true);
            }
        }
        catch {
            // Best effort cleanup
        }
        GC.SuppressFinalize(this);
    }
}
