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
        merged.AllowedExtensions.Should().Equal(ActionArray);
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
        merged.AllowedExtensions.Should().Equal(GlobalArray);
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
        merged.AllowedExtensions.Should().BeEmpty();
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
        merged.AllowedExtensions.Should().Equal(ActionArray);
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
        merged.AllowedExtensions.Should().BeEmpty("action null should fall back to global empty array");
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
        merged.ExcludePatterns.Should().Equal("*.bak");
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
        merged.ExcludePatterns.Should().Equal("*_g");
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
        merged.ExcludePatterns.Should().Equal("*.tmp");
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
        merged.ExcludePatterns.Should().BeEmpty();
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
        merged.ExcludePatterns.Should().BeEmpty("action null should fall back to global empty array");
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
        merged.ApiEndpoint.Should().Be("https://global/");
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
        merged.ApiEndpoint.Should().BeEmpty();
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
        merged.ProcessedFolder.Should().Be("processed_g");
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
        merged.ProcessedFolder.Should().BeEmpty();
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
        merged.PostFileContents.Should().BeTrue();
        merged.MoveProcessedFiles.Should().BeTrue();
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
        merged.DebounceMilliseconds.Should().Be(0);
        merged.StreamingThresholdBytes.Should().Be(0);
        merged.MaxContentBytes.Should().Be(0);
        merged.WaitForFileReadyMilliseconds.Should().Be(100);
        merged.Retries.Should().Be(3);
        merged.RetryDelayMilliseconds.Should().Be(0);
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
        merged1.BearerToken.Should().Be("globaltoken");

        cfg.Actions[0].BearerToken = string.Empty;
        Worker w2 = CreateWorker(cfg);
        ExternalConfiguration merged2 = MergeFor(w2, folder, "a.txt");
        merged2.BearerToken.Should().BeEmpty();
    }
}
