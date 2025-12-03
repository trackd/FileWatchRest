namespace FileWatchRest.Tests.Integration;

public class OverridesIntegrationFilteringTests {
    private sealed class TestDebounceService : FileDebounceService {
        public readonly List<string> Scheduled = [];
        public TestDebounceService() : base(new NullLogger<FileDebounceService>(), Channel.CreateUnbounded<string>().Writer, () => new ExternalConfiguration()) { }
        public override void Schedule(string path) => Scheduled.Add(path);
    }

    private static Worker CreateWorker(ExternalConfiguration cfg, TestDebounceService debounce) {
        var httpClientFactory = new HttpClientFactoryMock();
        var lifetime = new HostApplicationLifetimeMock();
        IOptionsMonitor<ExternalConfiguration> options = new SimpleOptionsMonitor<ExternalConfiguration>(cfg);
        var diagnostics = new DiagnosticsService(new NullLogger<DiagnosticsService>(), options);
        var manager = new FileWatcherManager(new NullLogger<FileWatcherManager>(), diagnostics);
        var resilience = new HttpResilienceService(new NullLogger<HttpResilienceService>(), diagnostics);
        var worker = new Worker(new NullLogger<Worker>(), httpClientFactory, lifetime, diagnostics, manager, debounce, resilience, options);
        return worker;
    }

    private static void InvokeOnFileChanged(Worker worker, string path) {
        MethodInfo mi = typeof(Worker).GetMethod("OnFileChanged", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var e = new FileSystemEventArgs(WatcherChangeTypes.Created, Path.GetDirectoryName(path)!, Path.GetFileName(path));
        mi.Invoke(worker, [null, e]);
    }

    [Fact]
    public void AllowedExtensions_action_filters_files_when_global_empty() {
        string folder = Path.Combine(Path.GetTempPath(), "over_int_1");
        Directory.CreateDirectory(folder);
        string allowed = Path.Combine(folder, "file.txt");
        string blocked = Path.Combine(folder, "file.bin");
        File.WriteAllText(allowed, "a");
        File.WriteAllText(blocked, "b");

        var cfg = new ExternalConfiguration {
            AllowedExtensions = [],
            ExcludePatterns = [],
            Folders = { new ExternalConfiguration.WatchedFolderConfig { FolderPath = folder, ActionName = "rest" } },
            Actions = { new ExternalConfiguration.ActionConfig { Name = "rest", ActionType = ExternalConfiguration.FolderActionType.RestPost, ApiEndpoint = "https://e/", AllowedExtensions = [".txt"] } }
        };
        var debounce = new TestDebounceService();
        Worker worker = CreateWorker(cfg, debounce);

        InvokeOnFileChanged(worker, allowed);
        InvokeOnFileChanged(worker, blocked);

        debounce.Scheduled.Should().Contain(allowed);
        debounce.Scheduled.Should().NotContain(blocked);
    }

    [Fact]
    public void ExcludePatterns_folder_override_blocks_matching_files() {
        string folder = Path.Combine(Path.GetTempPath(), "over_int_2");
        Directory.CreateDirectory(folder);
        string excluded = Path.Combine(folder, "report.bak");
        string included = Path.Combine(folder, "report.log");
        File.WriteAllText(excluded, "a");
        File.WriteAllText(included, "b");

        var cfg = new ExternalConfiguration {
            AllowedExtensions = [],
            ExcludePatterns = ["*.log"],
            Folders = { new ExternalConfiguration.WatchedFolderConfig { FolderPath = folder, ActionName = "rest" } },
            Actions = { new ExternalConfiguration.ActionConfig { Name = "rest", ActionType = ExternalConfiguration.FolderActionType.RestPost, ApiEndpoint = "https://e/", ExcludePatterns = ["*.bak"] } }
        };
        var debounce = new TestDebounceService();
        Worker worker = CreateWorker(cfg, debounce);

        InvokeOnFileChanged(worker, excluded);
        InvokeOnFileChanged(worker, included);

        debounce.Scheduled.Should().Contain(included);
        debounce.Scheduled.Should().NotContain(excluded);
    }

    [Fact]
    public void ProcessedFolder_ignored_by_worker() {
        string folder = Path.Combine(Path.GetTempPath(), "over_int_3");
        Directory.CreateDirectory(folder);
        string processedDir = Path.Combine(folder, "processed-folder");
        Directory.CreateDirectory(processedDir);
        string processedFile = Path.Combine(processedDir, "a.txt");
        File.WriteAllText(processedFile, "a");
        string normalFile = Path.Combine(folder, "b.txt");
        File.WriteAllText(normalFile, "b");

        var cfg = new ExternalConfiguration {
            AllowedExtensions = [],
            ExcludePatterns = [],
            Folders = { new ExternalConfiguration.WatchedFolderConfig { FolderPath = folder, ActionName = "rest" } },
            Actions = { new ExternalConfiguration.ActionConfig { Name = "rest", ActionType = ExternalConfiguration.FolderActionType.RestPost, ApiEndpoint = "https://e/", ProcessedFolder = "processed-folder" } }
        };
        var debounce = new TestDebounceService();
        Worker worker = CreateWorker(cfg, debounce);

        InvokeOnFileChanged(worker, processedFile);
        InvokeOnFileChanged(worker, normalFile);

        debounce.Scheduled.Should().Contain(normalFile);
        debounce.Scheduled.Should().NotContain(processedFile);
    }
}
