namespace FileWatchRest.Tests.Configuration;

public class OverridesBehaviorTests {
    private static readonly string[] EmptyArray = [];
    private static readonly string[] TxtArray = [".txt"];
    private static readonly string[] BinArray = [".bin"];
    private static readonly string[] CsvArray = [".csv"];
    private static readonly string[] LogArray = ["*.log"];
    private static readonly string[] BakArray = ["*.bak"];
    private static readonly string[] TmpArray = ["*_tmp"];

    private static Worker CreateWorker(ExternalConfiguration cfg) {
        var httpClientFactory = new HttpClientFactoryMock();
        var lifetime = new HostApplicationLifetimeMock();
        IOptionsMonitor<ExternalConfiguration> options = new SimpleOptionsMonitor<ExternalConfiguration>(cfg);
        var diagnostics = new DiagnosticsService(new NullLogger<DiagnosticsService>(), options);
        var manager = new FileWatcherManager(new NullLogger<FileWatcherManager>(), diagnostics);
        var ch = Channel.CreateUnbounded<string>();
        var debounce = new FileDebounceService(new NullLogger<FileDebounceService>(), ch.Writer, () => cfg);
        var resilience = new HttpResilienceService(new NullLogger<HttpResilienceService>(), diagnostics);
        var worker = new Worker(new NullLogger<Worker>(), httpClientFactory, lifetime, diagnostics, manager, debounce, resilience, options);
        return worker;
    }

    [Fact]
    public void ActionAllowedExtensions_applies_when_global_empty() {
        string folder = Path.Combine(Path.GetTempPath(), "fw_override_test");
        var cfg = new ExternalConfiguration {
            AllowedExtensions = Array.Empty<string>(),
            ExcludePatterns = EmptyArray,
            Folders = { new ExternalConfiguration.WatchedFolderConfig { FolderPath = folder, ActionName = "rest" } },
            Actions = { new ExternalConfiguration.ActionConfig { Name = "rest", ActionType = ExternalConfiguration.FolderActionType.RestPost, ApiEndpoint = "https://example/", AllowedExtensions = TxtArray } }
        };
        Worker worker = CreateWorker(cfg);

        string allowed = Path.Combine(folder, "file.txt");
        string rejected = Path.Combine(folder, "file.bin");

        ExternalConfiguration mergedAllowed = InvokeGetConfig(worker, allowed);
        ExternalConfiguration mergedRejected = InvokeGetConfig(worker, rejected);

        Assert.Equal(new[] { ".txt" }, mergedAllowed.AllowedExtensions);
        Assert.Equal(new[] { ".txt" }, mergedRejected.AllowedExtensions);
    }

    [Fact]
    public void ActionAllowedExtensions_wins_over_global() {
        string folder = Path.Combine(Path.GetTempPath(), "fw_override_test2");
        var cfg = new ExternalConfiguration {
            AllowedExtensions = BinArray,
            ExcludePatterns = EmptyArray,
            Folders = { new ExternalConfiguration.WatchedFolderConfig { FolderPath = folder, ActionName = "rest" } },
            Actions = { new ExternalConfiguration.ActionConfig { Name = "rest", ActionType = ExternalConfiguration.FolderActionType.RestPost, ApiEndpoint = "https://example/", AllowedExtensions = CsvArray } }
        };
        Worker worker = CreateWorker(cfg);

        string path = Path.Combine(folder, "whatever.any");
        ExternalConfiguration merged = InvokeGetConfig(worker, path);
        Assert.Equal(new[] { ".csv" }, merged.AllowedExtensions);
    }

    [Fact]
    public void Empty_action_extensions_means_no_filtering_even_if_global_has_values() {
        string folder = Path.Combine(Path.GetTempPath(), "fw_override_test3");
        var cfg = new ExternalConfiguration {
            AllowedExtensions = BinArray,
            ExcludePatterns = EmptyArray,
            Folders = { new ExternalConfiguration.WatchedFolderConfig { FolderPath = folder, ActionName = "rest" } },
            Actions = { new ExternalConfiguration.ActionConfig { Name = "rest", ActionType = ExternalConfiguration.FolderActionType.RestPost, ApiEndpoint = "https://example/", AllowedExtensions = Array.Empty<string>() } }
        };
        Worker worker = CreateWorker(cfg);

        ExternalConfiguration merged = InvokeGetConfig(worker, Path.Combine(folder, "file.any"));
        Assert.Empty(merged.AllowedExtensions);
    }

    [Fact]
    public void ExcludePatterns_action_over_global() {
        string folder = Path.Combine(Path.GetTempPath(), "fw_override_test4");
        var cfg = new ExternalConfiguration {
            AllowedExtensions = EmptyArray,
            ExcludePatterns = TmpArray,
            Folders = { new ExternalConfiguration.WatchedFolderConfig { FolderPath = folder, ActionName = "rest" } },
            Actions = { new ExternalConfiguration.ActionConfig { Name = "rest", ActionType = ExternalConfiguration.FolderActionType.RestPost, ApiEndpoint = "https://example/", ExcludePatterns = BakArray } }
        };
        Worker worker = CreateWorker(cfg);

        ExternalConfiguration merged = InvokeGetConfig(worker, Path.Combine(folder, "file.any"));
        Assert.Equal(new[] { "*.bak" }, merged.ExcludePatterns);
    }

    private static ExternalConfiguration InvokeGetConfig(Worker worker, string path) {
        MethodInfo mi = typeof(Worker).GetMethod("GetConfigForPath", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var result = (ExternalConfiguration)mi.Invoke(worker, [path])!;
        return result;
    }
}

// Minimal mocks used only for constructing Worker in tests
internal sealed class HttpClientFactoryMock : IHttpClientFactory {
    public HttpClient CreateClient(string name) => new();
}
internal sealed class HostApplicationLifetimeMock : IHostApplicationLifetime {
    public CancellationToken ApplicationStarted => CancellationToken.None;
    public CancellationToken ApplicationStopping => CancellationToken.None;
    public CancellationToken ApplicationStopped => CancellationToken.None;
    public void StopApplication() { }
}
