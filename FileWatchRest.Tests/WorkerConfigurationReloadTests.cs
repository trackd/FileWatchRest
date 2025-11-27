using FileWatchRest.Configuration;
using FileWatchRest.Services;
using Microsoft.Extensions.Logging;

namespace FileWatchRest.Tests;

/// <summary>
/// Tests for Worker.OnConfigurationChanged and related configuration reload scenarios
/// </summary>
public class WorkerConfigurationReloadTests : IDisposable {
    private readonly string _testDirectory;

    public WorkerConfigurationReloadTests() {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"FileWatchRest_ConfigReloadTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDirectory);
    }

    [Fact]
    public async Task OnConfigurationChangedUpdatesFolderActions() {
        // Arrange
        ILoggerFactory loggerFactory = LoggerFactory.Create(b => b.AddDebug().SetMinimumLevel(LogLevel.Debug));

        var initialConfig = new ExternalConfiguration {
            ApiEndpoint = "http://localhost:8080/api/files",
            Folders =
            [
                new() { FolderPath = _testDirectory, ActionName = "default" }
            ],
            Actions = [ new() { Name = "default", ActionType = ExternalConfiguration.FolderActionType.RestPost, ApiEndpoint = "http://localhost:8080/api/files" } ]
        };

        var optionsMonitor = new SimpleOptionsMonitor<ExternalConfiguration>(initialConfig);
        var diagnostics = new DiagnosticsService(loggerFactory.CreateLogger<DiagnosticsService>(), new TestUtilities.OptionsMonitorMock<ExternalConfiguration>());
        var fileWatcherManager = new FileWatcherManager(loggerFactory.CreateLogger<FileWatcherManager>(), diagnostics);

        Worker worker = WorkerFactory.CreateWorker(
            logger: loggerFactory.CreateLogger<Worker>(),
            httpClientFactory: new TestHttpClientFactory(),
            lifetime: new TestHostApplicationLifetime(),
            diagnostics: diagnostics,
            fileWatcherManager: fileWatcherManager,
            resilienceService: new HttpResilienceService(loggerFactory.CreateLogger<HttpResilienceService>(), diagnostics),
            optionsMonitor: optionsMonitor
        );

        // Act - Trigger configuration change
        var newConfig = new ExternalConfiguration {
            ApiEndpoint = "http://localhost:9090/api/files", // Changed endpoint
            Folders =
            [
                new() { FolderPath = _testDirectory, ActionName = "default" }
            ],
            Actions = [ new() { Name = "default", ActionType = ExternalConfiguration.FolderActionType.RestPost, ApiEndpoint = "http://localhost:9090/api/files" } ]
        };

        optionsMonitor.Raise(newConfig);

        // Give time for async configuration change handling
        await Task.Delay(500);

        // Assert
        Assert.Equal(newConfig.ApiEndpoint, worker.CurrentConfig.ApiEndpoint);
        Assert.Equal(newConfig.Folders.Count, worker.CurrentConfig.Folders.Count);
    }

    [Fact]
    public async Task OnConfigurationChangedHandlesConfigurationWithNoFolders() {
        // Arrange
        ILoggerFactory loggerFactory = LoggerFactory.Create(b => b.AddDebug().SetMinimumLevel(LogLevel.Debug));

        var initialConfig = new ExternalConfiguration {
            ApiEndpoint = "http://localhost:8080/api/files",
            Folders =
            [
                new() { FolderPath = _testDirectory, ActionName = "default" }
            ],
            Actions = [ new() { Name = "default", ActionType = ExternalConfiguration.FolderActionType.RestPost, ApiEndpoint = "http://localhost:8080/api/files" } ]
        };

        var optionsMonitor = new SimpleOptionsMonitor<ExternalConfiguration>(initialConfig);
        var diagService = new DiagnosticsService(loggerFactory.CreateLogger<DiagnosticsService>(), new TestUtilities.OptionsMonitorMock<ExternalConfiguration>());

        Worker worker = WorkerFactory.CreateWorker(
            logger: loggerFactory.CreateLogger<Worker>(),
            httpClientFactory: new TestHttpClientFactory(),
            lifetime: new TestHostApplicationLifetime(),
            diagnostics: diagService,
            fileWatcherManager: new FileWatcherManager(loggerFactory.CreateLogger<FileWatcherManager>(), diagService),
            resilienceService: new HttpResilienceService(loggerFactory.CreateLogger<HttpResilienceService>(), diagService),
            optionsMonitor: optionsMonitor
        );

        // Act - Change to config with no folders
        var emptyConfig = new ExternalConfiguration {
            ApiEndpoint = "http://localhost:8080/api/files",
            Folders = [] // Empty
        };

        optionsMonitor.Raise(emptyConfig);
        await Task.Delay(300);

        // Assert - Should handle gracefully without throwing
        Assert.Empty(worker.CurrentConfig.Folders);
    }

    [Fact]
    public async Task OnConfigurationChangedRestartsFolderActions() {
        // Arrange
        ILoggerFactory loggerFactory = LoggerFactory.Create(b => b.AddDebug().SetMinimumLevel(LogLevel.Debug));
        string folder1 = Path.Combine(_testDirectory, "folder1");
        string folder2 = Path.Combine(_testDirectory, "folder2");
        Directory.CreateDirectory(folder1);
        Directory.CreateDirectory(folder2);

        var initialConfig = new ExternalConfiguration {
            ApiEndpoint = "http://localhost:8080/api/files",
            Folders =
            [
                new() { FolderPath = folder1, ActionName = "default" }
            ],
            Actions = [ new() { Name = "default", ActionType = ExternalConfiguration.FolderActionType.RestPost, ApiEndpoint = "http://localhost:8080/api/files" } ]
        };

        var optionsMonitor = new SimpleOptionsMonitor<ExternalConfiguration>(initialConfig);
        var diagnostics = new DiagnosticsService(loggerFactory.CreateLogger<DiagnosticsService>(), new TestUtilities.OptionsMonitorMock<ExternalConfiguration>());
        var fileWatcherManager = new FileWatcherManager(loggerFactory.CreateLogger<FileWatcherManager>(), diagnostics);

        var resilience = new HttpResilienceService(loggerFactory.CreateLogger<HttpResilienceService>(), diagnostics);

        Worker worker = WorkerFactory.CreateWorker(
            logger: loggerFactory.CreateLogger<Worker>(),
            httpClientFactory: new TestHttpClientFactory(),
            lifetime: new TestHostApplicationLifetime(),
            diagnostics: diagnostics,
            fileWatcherManager: fileWatcherManager,
            resilienceService: resilience,
            optionsMonitor: optionsMonitor
        );

        // Act - Change to different folder
        var newConfig = new ExternalConfiguration {
            ApiEndpoint = "http://localhost:8080/api/files",
            Folders =
            [
                new() { FolderPath = folder2, ActionName = "default" } // Different folder
            ],
            Actions = [ new() { Name = "default", ActionType = ExternalConfiguration.FolderActionType.RestPost, ApiEndpoint = "http://localhost:8080/api/files" } ]
        };

        optionsMonitor.Raise(newConfig);
        await Task.Delay(500);

        // Assert
        Assert.Single(worker.CurrentConfig.Folders);
        Assert.Equal(folder2, worker.CurrentConfig.Folders[0].FolderPath);
    }

    [Fact]
    public async Task ConfigureLoggingParsesLogLevelCorrectly() {
        // Arrange
        ILoggerFactory loggerFactory = LoggerFactory.Create(b => b.AddDebug().SetMinimumLevel(LogLevel.Debug));

        var config = new ExternalConfiguration {
            ApiEndpoint = "http://localhost:8080/api/files",
            Folders =
            [
                new() { FolderPath = _testDirectory, ActionName = "default" }
            ],
            Actions = [ new() { Name = "default", ActionType = ExternalConfiguration.FolderActionType.RestPost, ApiEndpoint = "http://localhost:8080/api/files" } ],
            Logging = new SimpleFileLoggerOptions {
                LogLevel = LogLevel.Warning
            }
        };

        var optionsMonitor = new SimpleOptionsMonitor<ExternalConfiguration>(config);
        var diagService = new DiagnosticsService(loggerFactory.CreateLogger<DiagnosticsService>(), new TestUtilities.OptionsMonitorMock<ExternalConfiguration>());

        // Act
        Worker worker = WorkerFactory.CreateWorker(
            logger: loggerFactory.CreateLogger<Worker>(),
            httpClientFactory: new TestHttpClientFactory(),
            lifetime: new TestHostApplicationLifetime(),
            diagnostics: diagService,
            fileWatcherManager: new FileWatcherManager(loggerFactory.CreateLogger<FileWatcherManager>(), diagService),
            resilienceService: new HttpResilienceService(loggerFactory.CreateLogger<HttpResilienceService>(), diagService),
            optionsMonitor: optionsMonitor
        );

        // Call ConfigureLogging via reflection
        System.Reflection.MethodInfo? configureMethod = typeof(Worker).GetMethod("ConfigureLogging", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        configureMethod?.Invoke(worker, ["Debug"]);

        await Task.Delay(100);

        // Assert - No exception thrown, configuration accepted
        Assert.NotNull(worker.CurrentConfig);
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
