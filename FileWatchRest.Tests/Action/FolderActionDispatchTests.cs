namespace FileWatchRest.Tests;

public class FolderActionDispatchTests {
    [Fact(DisplayName = "ConfigureFolderActions_MapsActionsCorrectly")]
    public void ConfigureFolderActionsMapsActionsCorrectly() {
        ILoggerFactory loggerFactory = LoggerFactory.Create(b => b.AddDebug());
        var diagnostics = new DiagnosticsService(loggerFactory.CreateLogger<DiagnosticsService>(), new OptionsMonitorMock<ExternalConfiguration>());
        var manager = new FileWatcherManager(loggerFactory.CreateLogger<FileWatcherManager>(), diagnostics);
        var worker = new TestWorker();

        var configs = new List<ExternalConfiguration.WatchedFolderConfig>
        {
                new() { FolderPath = "C:/test1", ActionName = "rest-action" },
                new() { FolderPath = "C:/test2", ActionName = "ps-action" }
            };

        var globalConfig = new ExternalConfiguration {
            Actions = [
                new() { Name = "rest-action", ActionType = ExternalConfiguration.FolderActionType.RestPost },
                new() { Name = "ps-action", ActionType = ExternalConfiguration.FolderActionType.PowerShellScript, ScriptPath = "C:/script.ps1" }
            ]
        };

        manager.ConfigureFolderActions(configs, globalConfig, worker);

        Dictionary<string, List<IFolderAction>> dict = manager._folderActions;

        Assert.True(dict.ContainsKey("C:/test1"));
        Assert.True(dict.ContainsKey("C:/test2"));
        Assert.Single(dict["C:/test1"]);
        Assert.Single(dict["C:/test2"]);
        Assert.IsType<RestPostAction>(dict["C:/test1"][0]);
        Assert.IsType<PowerShellScriptAction>(dict["C:/test2"][0]);
    }

    [Fact(DisplayName = "HandleFileEvent_ExecutesMappedActions")]
    public async Task HandleFileEventExecutesMappedActions() {
        ILoggerFactory loggerFactory = LoggerFactory.Create(b => b.AddDebug());
        var diagnostics = new DiagnosticsService(loggerFactory.CreateLogger<DiagnosticsService>(), new OptionsMonitorMock<ExternalConfiguration>());
        var manager = new FileWatcherManager(loggerFactory.CreateLogger<FileWatcherManager>(), diagnostics);

        bool called = false;
        var mockAction = new MockFolderAction(() => called = true);
        manager._folderActions.Clear();
        manager._folderActions["C:/test"] = [mockAction];

        manager.HandleFileEvent("C:/test", new FileSystemEventArgs(WatcherChangeTypes.Created, "C:/test", "file.txt"));

        await Task.Delay(100); // Allow async action to run
        Assert.True(called);
    }

    private sealed class MockFolderAction(System.Action onExecute) : IFolderAction {
        private readonly System.Action _onExecute = onExecute;

        public Task ExecuteAsync(FileEventRecord fileEvent, CancellationToken cancellationToken) {
            _onExecute();
            return Task.CompletedTask;
        }
    }

    private sealed class TestWorker : Worker {
        public TestWorker() : base(
            NullLogger<Worker>.Instance,
            new SimpleHttpClientFactory(),
            new TestHostLifetime(),
            new DiagnosticsService(NullLogger<DiagnosticsService>.Instance, new OptionsMonitorMock<ExternalConfiguration>()),
            new FileWatcherManager(NullLogger<FileWatcherManager>.Instance, new DiagnosticsService(NullLogger<DiagnosticsService>.Instance, new OptionsMonitorMock<ExternalConfiguration>())),
            new FileDebounceServiceMock(),
            new TestResilienceService(),
            new SimpleOptionsMonitor<ExternalConfiguration>(new ExternalConfiguration())) { }
    }

    private sealed class SimpleHttpClientFactory : IHttpClientFactory { public HttpClient CreateClient(string name) => new(); }
    private sealed class TestHostLifetime : IHostApplicationLifetime { public CancellationToken ApplicationStarted => CancellationToken.None; public CancellationToken ApplicationStopping => CancellationToken.None; public CancellationToken ApplicationStopped => CancellationToken.None; public void StopApplication() { } }
    private sealed class TestResilienceService : IResilienceService { public Task<ResilienceResult> SendWithRetriesAsync(Func<CancellationToken, Task<HttpRequestMessage>> requestFactory, HttpClient client, string endpointKey, ExternalConfiguration config, CancellationToken ct) => Task.FromResult(new ResilienceResult(true, 1, 200, null, 10, false)); }
}
