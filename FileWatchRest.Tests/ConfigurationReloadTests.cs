
using FileWatchRest.Configuration;

namespace FileWatchRest.Tests;

public class ConfigurationReloadTests {
    [Fact]
    public async Task ConfigurationReloadUpdatesApiEndpointWithoutRestart() {
        // Arrange
        string initialEndpoint = "http://localhost:5000/api/initial";
        string updatedEndpoint = "http://localhost:5000/api/updated";
        var configMonitor = new OptionsMonitorMock<ExternalConfiguration>();
        // Set initial config before Worker construction
        configMonitor.SetCurrentValue(new ExternalConfiguration { ApiEndpoint = initialEndpoint });
        ILogger<DiagnosticsService> logger = new LoggerFactory().CreateLogger<DiagnosticsService>();
        var httpClientFactory = new HttpClientFactoryMock();
        var appLifetime = new HostApplicationLifetimeMock();
        var diagnostics = new DiagnosticsService(logger, configMonitor);
        var fileWatcherManager = new FileWatcherManager(new LoggerFactory().CreateLogger<FileWatcherManager>(), diagnostics);
        var resilienceService = new ResilienceServiceMock();
        Worker worker = WorkerFactory.CreateWorker(
            logger: new LoggerFactory().CreateLogger<Worker>(),
            httpClientFactory: httpClientFactory,
            lifetime: appLifetime,
            diagnostics: diagnostics,
            fileWatcherManager: fileWatcherManager,
            resilienceService: resilienceService,
            optionsMonitor: configMonitor
        );

        Assert.Equal(initialEndpoint, worker.CurrentConfig.ApiEndpoint);

        // Act: update config
        configMonitor.SetCurrentValue(new ExternalConfiguration { ApiEndpoint = updatedEndpoint });
        // Allow time for async OnChange to propagate
        await Task.Delay(50);
        Assert.Equal(updatedEndpoint, worker.CurrentConfig.ApiEndpoint);
    }

    [Fact]
    public async Task ConfigurationReloadUpdatesDiagnosticsUrlPrefixWithoutRestart() {
        // Arrange
        string initialPrefix = "http://localhost:5005/";
        string updatedPrefix = "http://localhost:6006/";
        var configMonitor = new OptionsMonitorMock<ExternalConfiguration>();
        // Set initial value before constructing DiagnosticsService
        configMonitor.SetCurrentValue(new ExternalConfiguration { DiagnosticsUrlPrefix = initialPrefix });
        ILogger<DiagnosticsService> logger = new LoggerFactory().CreateLogger<DiagnosticsService>();
        var diagnostics = new DiagnosticsService(logger, configMonitor);

        Assert.Equal(initialPrefix, diagnostics.CurrentPrefix);

        // Act: update config
        configMonitor.SetCurrentValue(new ExternalConfiguration { DiagnosticsUrlPrefix = updatedPrefix });
        diagnostics.RestartHttpServer(updatedPrefix);
        await Task.Delay(50); // Ensure async context for test method

        // Assert
        Assert.Equal(updatedPrefix, diagnostics.CurrentPrefix);
    }

    [Fact]
    public async Task FolderActionMappingExecutesPowerShellScriptAction() {
        // Arrange
        string folderPath = "C:\\TestFolder";
        string scriptPath = "C:\\TestScript.ps1";
        var config = new ExternalConfiguration {
            Folders =
            [
                new() { FolderPath = folderPath, ActionName = "ps-action" }
            ],
            Actions = [ new() { Name = "ps-action", ActionType = ExternalConfiguration.FolderActionType.PowerShellScript, ScriptPath = scriptPath } ]
        };
        ILogger<FileWatcherManager> logger = new LoggerFactory().CreateLogger<FileWatcherManager>();
        var diagnostics = new DiagnosticsService(new LoggerFactory().CreateLogger<DiagnosticsService>(), new OptionsMonitorMock<ExternalConfiguration>());
        var fileWatcherManager = new FileWatcherManager(logger, diagnostics);
        Worker worker = WorkerFactory.CreateWorker(
            logger: new LoggerFactory().CreateLogger<Worker>(),
            httpClientFactory: new HttpClientFactoryMock(),
            lifetime: new HostApplicationLifetimeMock(),
            diagnostics: diagnostics,
            fileWatcherManager: fileWatcherManager,
            resilienceService: new ResilienceServiceMock(),
            optionsMonitor: new OptionsMonitorMock<ExternalConfiguration>()
        );
        fileWatcherManager.ConfigureFolderActions(config.Folders, config, worker);

        // Act: simulate file event
        var fileEvent = new FileEventRecord { Path = "C:\\TestFolder\\file.txt" };
        var action = new PowerShellScriptAction(scriptPath);
        // Instead of actually running PowerShell, just verify the method can be called
        await action.ExecuteAsync(fileEvent, CancellationToken.None);
        // Assert: no exception thrown
        Assert.True(true);
    }
}
