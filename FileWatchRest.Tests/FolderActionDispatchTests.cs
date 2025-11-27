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
                new() { FolderPath = "C:/test1", ActionType = ExternalConfiguration.FolderActionType.RestPost },
                new() { FolderPath = "C:/test2", ActionType = ExternalConfiguration.FolderActionType.PowerShellScript, ScriptPath = "C:/script.ps1" }
            };

        manager.ConfigureFolderActions(configs, worker);

        System.Reflection.FieldInfo? field = typeof(FileWatcherManager).GetField("_folderActions", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var dict = (Dictionary<string, List<IFolderAction>>)field!.GetValue(manager)!;

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
        System.Reflection.FieldInfo? field = typeof(FileWatcherManager).GetField("_folderActions", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var dict = new Dictionary<string, List<IFolderAction>> { ["C:/test"] = [mockAction] };
        field!.SetValue(manager, dict);

        System.Reflection.MethodInfo? method = typeof(FileWatcherManager).GetMethod("HandleFileEvent", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        method!.Invoke(manager, ["C:/test", new FileSystemEventArgs(WatcherChangeTypes.Created, "C:/test", "file.txt")]);

        await Task.Delay(100); // Allow async action to run
        Assert.True(called);
    }

    private sealed class MockFolderAction(Action onExecute) : IFolderAction {
        private readonly Action _onExecute = onExecute;

        public Task ExecuteAsync(FileEventRecord fileEvent, CancellationToken cancellationToken) {
            _onExecute();
            return Task.CompletedTask;
        }
    }

    private sealed class TestWorker : Worker {
        public TestWorker() : base(
            NullLogger<Worker>.Instance,
            null!, null!,
            new DiagnosticsService(NullLogger<DiagnosticsService>.Instance, new OptionsMonitorMock<ExternalConfiguration>()),
            new FileWatcherManager(NullLogger<FileWatcherManager>.Instance, new DiagnosticsService(NullLogger<DiagnosticsService>.Instance, new OptionsMonitorMock<ExternalConfiguration>())),
            new FileDebounceServiceMock(),
            null!,
            new SimpleOptionsMonitor<ExternalConfiguration>(new ExternalConfiguration())) { }
    }
}
