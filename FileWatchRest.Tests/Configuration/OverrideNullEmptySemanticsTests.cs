namespace FileWatchRest.Tests.Configuration;

public class OverrideNullEmptySemanticsTests {
    private static readonly string[] GlobalArray = [".global1", ".global2"];
    private static readonly string[] ActionArray = [".action1", ".action2"];
    private static readonly string[] EmptyArray = [];

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

    private static ExternalConfiguration MergeFor(Worker w, string folder, string fileName) {
        MethodInfo mi = typeof(Worker).GetMethod("GetConfigForPath", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var result = (ExternalConfiguration)mi.Invoke(w, [Path.Combine(folder, fileName)])!;
        return result;
    }

    [Fact]
    public void AllowedExtensions_GlobalNull_ActionHasValues_UsesAction() {
        string folder = Path.Combine(Path.GetTempPath(), "nullsem_1");
        var cfg = new ExternalConfiguration {
            AllowedExtensions = null!,
            Folders = { new ExternalConfiguration.WatchedFolderConfig { FolderPath = folder, ActionName = "A" } },
            Actions = { new ExternalConfiguration.ActionConfig { Name = "A", ActionType = ExternalConfiguration.FolderActionType.RestPost, ApiEndpoint = "https://e/", AllowedExtensions = ActionArray } }
        };
        Worker w = CreateWorker(cfg);
        ExternalConfiguration merged = MergeFor(w, folder, "a.txt");
        Assert.Equal(ActionArray, merged.AllowedExtensions);
    }

    [Fact]
    public void AllowedExtensions_GlobalHasValues_ActionNull_UsesGlobal() {
        string folder = Path.Combine(Path.GetTempPath(), "nullsem_2");
        var cfg = new ExternalConfiguration {
            AllowedExtensions = GlobalArray,
            Folders = { new ExternalConfiguration.WatchedFolderConfig { FolderPath = folder, ActionName = "A" } },
            Actions = { new ExternalConfiguration.ActionConfig { Name = "A", ActionType = ExternalConfiguration.FolderActionType.RestPost, ApiEndpoint = "https://e/", AllowedExtensions = null } }
        };
        Worker w = CreateWorker(cfg);
        ExternalConfiguration merged = MergeFor(w, folder, "a.txt");
        Assert.Equal(GlobalArray, merged.AllowedExtensions);
    }

    [Fact]
    public void AllowedExtensions_ActionEmpty_DisablesFiltering() {
        string folder = Path.Combine(Path.GetTempPath(), "nullsem_3");
        var cfg = new ExternalConfiguration {
            AllowedExtensions = GlobalArray,
            Folders = { new ExternalConfiguration.WatchedFolderConfig { FolderPath = folder, ActionName = "A" } },
            Actions = { new ExternalConfiguration.ActionConfig { Name = "A", ActionType = ExternalConfiguration.FolderActionType.RestPost, ApiEndpoint = "https://e/", AllowedExtensions = EmptyArray } }
        };
        Worker w = CreateWorker(cfg);
        ExternalConfiguration merged = MergeFor(w, folder, "a.txt");
        Assert.Empty(merged.AllowedExtensions);
    }

    [Fact]
    public void AllowedExtensions_GlobalEmpty_ActionHasValues_UsesAction() {
        string folder = Path.Combine(Path.GetTempPath(), "nullsem_3b");
        var cfg = new ExternalConfiguration {
            AllowedExtensions = EmptyArray,
            Folders = { new ExternalConfiguration.WatchedFolderConfig { FolderPath = folder, ActionName = "A" } },
            Actions = { new ExternalConfiguration.ActionConfig { Name = "A", ActionType = ExternalConfiguration.FolderActionType.RestPost, ApiEndpoint = "https://e/", AllowedExtensions = ActionArray } }
        };
        Worker w = CreateWorker(cfg);
        ExternalConfiguration merged = MergeFor(w, folder, "a.txt");
        Assert.Equal(ActionArray, merged.AllowedExtensions);
    }

    [Fact]
    public void AllowedExtensions_GlobalEmpty_ActionNull_UsesGlobalEmpty() {
        string folder = Path.Combine(Path.GetTempPath(), "nullsem_3c");
        var cfg = new ExternalConfiguration {
            AllowedExtensions = EmptyArray,
            Folders = { new ExternalConfiguration.WatchedFolderConfig { FolderPath = folder, ActionName = "A" } },
            Actions = { new ExternalConfiguration.ActionConfig { Name = "A", ActionType = ExternalConfiguration.FolderActionType.RestPost, ApiEndpoint = "https://e/", AllowedExtensions = null } }
        };
        Worker w = CreateWorker(cfg);
        ExternalConfiguration merged = MergeFor(w, folder, "a.txt");
        Assert.Empty(merged.AllowedExtensions);
    }

    [Fact]
    public void ExcludePatterns_GlobalNull_ActionHasValues_UsesAction() {
        string folder = Path.Combine(Path.GetTempPath(), "nullsem_4");
        var cfg = new ExternalConfiguration {
            ExcludePatterns = null!,
            Folders = { new ExternalConfiguration.WatchedFolderConfig { FolderPath = folder, ActionName = "A" } },
            Actions = { new ExternalConfiguration.ActionConfig { Name = "A", ActionType = ExternalConfiguration.FolderActionType.RestPost, ApiEndpoint = "https://e/", ExcludePatterns = ["*.bak"] } }
        };
        Worker w = CreateWorker(cfg);
        ExternalConfiguration merged = MergeFor(w, folder, "file.bak");
        Assert.Equal(new[] { "*.bak" }, merged.ExcludePatterns);
    }

    [Fact]
    public void ExcludePatterns_GlobalHasValues_ActionNull_UsesGlobal() {
        string folder = Path.Combine(Path.GetTempPath(), "nullsem_5");
        var cfg = new ExternalConfiguration {
            ExcludePatterns = ["*_g"],
            Folders = { new ExternalConfiguration.WatchedFolderConfig { FolderPath = folder, ActionName = "A" } },
            Actions = { new ExternalConfiguration.ActionConfig { Name = "A", ActionType = ExternalConfiguration.FolderActionType.RestPost, ApiEndpoint = "https://e/", ExcludePatterns = null } }
        };
        Worker w = CreateWorker(cfg);
        ExternalConfiguration merged = MergeFor(w, folder, "f.txt");
        Assert.Equal(new[] { "*_g" }, merged.ExcludePatterns);
    }

    [Fact]
    public void ExcludePatterns_GlobalEmpty_ActionHasValues_UsesAction() {
        string folder = Path.Combine(Path.GetTempPath(), "nullsem_5b");
        var cfg = new ExternalConfiguration {
            ExcludePatterns = EmptyArray,
            Folders = { new ExternalConfiguration.WatchedFolderConfig { FolderPath = folder, ActionName = "A" } },
            Actions = { new ExternalConfiguration.ActionConfig { Name = "A", ActionType = ExternalConfiguration.FolderActionType.RestPost, ApiEndpoint = "https://e/", ExcludePatterns = ["*.tmp"] } }
        };
        Worker w = CreateWorker(cfg);
        ExternalConfiguration merged = MergeFor(w, folder, "file.tmp");
        Assert.Equal(new[] { "*.tmp" }, merged.ExcludePatterns);
    }

    [Fact]
    public void ExcludePatterns_ActionEmpty_DisablesFiltering() {
        string folder = Path.Combine(Path.GetTempPath(), "nullsem_5c");
        var cfg = new ExternalConfiguration {
            ExcludePatterns = ["*.bak"],
            Folders = { new ExternalConfiguration.WatchedFolderConfig { FolderPath = folder, ActionName = "A" } },
            Actions = { new ExternalConfiguration.ActionConfig { Name = "A", ActionType = ExternalConfiguration.FolderActionType.RestPost, ApiEndpoint = "https://e/", ExcludePatterns = EmptyArray } }
        };
        Worker w = CreateWorker(cfg);
        ExternalConfiguration merged = MergeFor(w, folder, "file.bak");
        Assert.Empty(merged.ExcludePatterns);
    }

    [Fact]
    public void ExcludePatterns_GlobalEmpty_ActionNull_UsesGlobalEmpty() {
        string folder = Path.Combine(Path.GetTempPath(), "nullsem_7b");
        var cfg = new ExternalConfiguration {
            ExcludePatterns = EmptyArray,
            Folders = { new ExternalConfiguration.WatchedFolderConfig { FolderPath = folder, ActionName = "A" } },
            Actions = { new ExternalConfiguration.ActionConfig { Name = "A", ActionType = ExternalConfiguration.FolderActionType.RestPost, ApiEndpoint = "https://e/", ExcludePatterns = null } }
        };
        Worker w = CreateWorker(cfg);
        ExternalConfiguration merged = MergeFor(w, folder, "file.bak");
        Assert.Empty(merged.ExcludePatterns);
    }

    [Fact]
    public void ApiEndpoint_GlobalSet_ActionNull_UsesGlobal() {
        string folder = Path.Combine(Path.GetTempPath(), "nullsem_6");
        var cfg = new ExternalConfiguration {
            ApiEndpoint = "https://global/",
            Folders = { new ExternalConfiguration.WatchedFolderConfig { FolderPath = folder, ActionName = "A" } },
            Actions = { new ExternalConfiguration.ActionConfig { Name = "A", ActionType = ExternalConfiguration.FolderActionType.RestPost, ApiEndpoint = null } }
        };
        Worker w = CreateWorker(cfg);
        ExternalConfiguration merged = MergeFor(w, folder, "a.txt");
        Assert.Equal("https://global/", merged.ApiEndpoint);
    }

    [Fact]
    public void ApiEndpoint_GlobalSet_ActionEmpty_OverridesToEmpty() {
        string folder = Path.Combine(Path.GetTempPath(), "nullsem_7");
        var cfg = new ExternalConfiguration {
            ApiEndpoint = "https://global/",
            Folders = { new ExternalConfiguration.WatchedFolderConfig { FolderPath = folder, ActionName = "A" } },
            Actions = { new ExternalConfiguration.ActionConfig { Name = "A", ActionType = ExternalConfiguration.FolderActionType.RestPost, ApiEndpoint = string.Empty } }
        };
        Worker w = CreateWorker(cfg);
        ExternalConfiguration merged = MergeFor(w, folder, "a.txt");
        Assert.Equal(string.Empty, merged.ApiEndpoint);
    }

    [Fact]
    public void ProcessedFolder_GlobalSet_ActionNull_UsesGlobal() {
        string folder = Path.Combine(Path.GetTempPath(), "nullsem_8");
        var cfg = new ExternalConfiguration {
            ProcessedFolder = "processed_g",
            Folders = { new ExternalConfiguration.WatchedFolderConfig { FolderPath = folder, ActionName = "A" } },
            Actions = { new ExternalConfiguration.ActionConfig { Name = "A", ActionType = ExternalConfiguration.FolderActionType.RestPost, ProcessedFolder = null } }
        };
        Worker w = CreateWorker(cfg);
        ExternalConfiguration merged = MergeFor(w, folder, "a.txt");
        Assert.Equal("processed_g", merged.ProcessedFolder);
    }

    [Fact]
    public void ProcessedFolder_GlobalSet_ActionEmpty_OverridesToEmpty() {
        string folder = Path.Combine(Path.GetTempPath(), "nullsem_9");
        var cfg = new ExternalConfiguration {
            ProcessedFolder = "processed_g",
            Folders = { new ExternalConfiguration.WatchedFolderConfig { FolderPath = folder, ActionName = "A" } },
            Actions = { new ExternalConfiguration.ActionConfig { Name = "A", ActionType = ExternalConfiguration.FolderActionType.RestPost, ProcessedFolder = string.Empty } }
        };
        Worker w = CreateWorker(cfg);
        ExternalConfiguration merged = MergeFor(w, folder, "a.txt");
        Assert.Empty(merged.ProcessedFolder);
    }

    [Fact]
    public void BooleanOverrides_ActionNull_FallsBack_ActionSpecified_Wins() {
        string folder = Path.Combine(Path.GetTempPath(), "nullsem_10");
        var cfg = new ExternalConfiguration {
            PostFileContents = true,
            MoveProcessedFiles = false,
            Folders = { new ExternalConfiguration.WatchedFolderConfig { FolderPath = folder, ActionName = "A" } },
            Actions = { new ExternalConfiguration.ActionConfig { Name = "A", ActionType = ExternalConfiguration.FolderActionType.RestPost, PostFileContents = null, MoveProcessedFiles = true } }
        };
        Worker w = CreateWorker(cfg);
        ExternalConfiguration merged = MergeFor(w, folder, "a.txt");
        Assert.True(merged.PostFileContents);
        Assert.True(merged.MoveProcessedFiles);
    }

    [Fact]
    public void NumericZeroOverrides_ActionZero_Wins_ActionNull_FallsBack() {
        string folder = Path.Combine(Path.GetTempPath(), "nullsem_11");
        var cfg = new ExternalConfiguration {
            DebounceMilliseconds = 250,
            StreamingThresholdBytes = 1024,
            MaxContentBytes = 10_000,
            WaitForFileReadyMilliseconds = 100,
            Retries = 3,
            RetryDelayMilliseconds = 500,
            Folders = { new ExternalConfiguration.WatchedFolderConfig { FolderPath = folder, ActionName = "A" } },
            Actions = { new ExternalConfiguration.ActionConfig { Name = "A", ActionType = ExternalConfiguration.FolderActionType.RestPost, DebounceMilliseconds = 0, StreamingThresholdBytes = 0, MaxContentBytes = 0, WaitForFileReadyMilliseconds = null, Retries = null, RetryDelayMilliseconds = 0 } }
        };
        Worker w = CreateWorker(cfg);
        ExternalConfiguration merged = MergeFor(w, folder, "a.txt");
        Assert.Equal(0, merged.DebounceMilliseconds);
        Assert.Equal(0, merged.StreamingThresholdBytes);
        Assert.Equal(0, merged.MaxContentBytes);
        Assert.Equal(100, merged.WaitForFileReadyMilliseconds);
        Assert.Equal(3, merged.Retries);
        Assert.Equal(0, merged.RetryDelayMilliseconds);
    }

    [Fact]
    public void BearerToken_GlobalSet_ActionNull_UsesGlobal_ActionEmpty_OverridesToEmpty() {
        string folder = Path.Combine(Path.GetTempPath(), "nullsem_12");
        var cfg = new ExternalConfiguration {
            ApiEndpoint = "https://e/",
            BearerToken = "globaltoken",
            Folders = { new ExternalConfiguration.WatchedFolderConfig { FolderPath = folder, ActionName = "A" } },
            Actions = { new ExternalConfiguration.ActionConfig { Name = "A", ActionType = ExternalConfiguration.FolderActionType.RestPost, BearerToken = null } }
        };
        Worker w1 = CreateWorker(cfg);
        ExternalConfiguration merged1 = MergeFor(w1, folder, "a.txt");
        Assert.Equal("globaltoken", merged1.BearerToken);

        cfg.Actions[0].BearerToken = string.Empty;
        Worker w2 = CreateWorker(cfg);
        ExternalConfiguration merged2 = MergeFor(w2, folder, "a.txt");
        Assert.Equal(string.Empty, merged2.BearerToken);
    }
}
