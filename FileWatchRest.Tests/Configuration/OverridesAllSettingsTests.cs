using System.IO;
using FileWatchRest.Configuration;
using FileWatchRest.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FileWatchRest.Tests.Configuration;

public class OverridesAllSettingsTests {
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
        System.Reflection.MethodInfo mi = typeof(Worker).GetMethod("GetConfigForPath", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        ExternalConfiguration result = (ExternalConfiguration)mi.Invoke(w, new object[] { Path.Combine(folder, fileName) })!;
        return result;
    }

    [Fact]
    public void PostFileContents_Override_Action_Global_Priority() {
        string folder = Path.Combine(Path.GetTempPath(), "over_all_1");
        var cfg = new ExternalConfiguration {
            PostFileContents = false,
            Folders = { new ExternalConfiguration.WatchedFolderConfig { FolderPath = folder, ActionName = "rest" } },
            Actions = { new ExternalConfiguration.ActionConfig { Name = "rest", ActionType = ExternalConfiguration.FolderActionType.RestPost, ApiEndpoint = "https://e/", PostFileContents = true } }
        };
        Worker w = CreateWorker(cfg);
        ExternalConfiguration merged = MergeFor(w, folder, "a.txt");
        merged.PostFileContents.Should().BeTrue();
    }

    [Fact]
    public void MoveProcessedFiles_Override_Action_Global_Priority() {
        string folder = Path.Combine(Path.GetTempPath(), "over_all_2");
        var cfg = new ExternalConfiguration {
            MoveProcessedFiles = false,
            Folders = { new ExternalConfiguration.WatchedFolderConfig { FolderPath = folder, ActionName = "rest" } },
            Actions = { new ExternalConfiguration.ActionConfig { Name = "rest", ActionType = ExternalConfiguration.FolderActionType.RestPost, ApiEndpoint = "https://e/", MoveProcessedFiles = true } }
        };
        Worker w = CreateWorker(cfg);
        ExternalConfiguration merged = MergeFor(w, folder, "a.txt");
        merged.MoveProcessedFiles.Should().BeTrue();
    }

    [Fact]
    public void ProcessedFolder_Override_Action_Global_Priority() {
        string folder = Path.Combine(Path.GetTempPath(), "over_all_3");
        var cfg = new ExternalConfiguration {
            ProcessedFolder = "processed-global",
            Folders = { new ExternalConfiguration.WatchedFolderConfig { FolderPath = folder, ActionName = "rest" } },
            Actions = { new ExternalConfiguration.ActionConfig { Name = "rest", ActionType = ExternalConfiguration.FolderActionType.RestPost, ApiEndpoint = "https://e/", ProcessedFolder = "processed-folder" } }
        };
        Worker w = CreateWorker(cfg);
        ExternalConfiguration merged = MergeFor(w, folder, "a.txt");
        merged.ProcessedFolder.Should().Be("processed-folder");
    }

    [Fact]
    public void AllowedExtensions_Override_Action_Global_Priority() {
        string folder = Path.Combine(Path.GetTempPath(), "over_all_4");
        var cfg = new ExternalConfiguration {
            AllowedExtensions = [".global"],
            Folders = { new ExternalConfiguration.WatchedFolderConfig { FolderPath = folder, ActionName = "rest" } },
            Actions = { new ExternalConfiguration.ActionConfig { Name = "rest", ActionType = ExternalConfiguration.FolderActionType.RestPost, ApiEndpoint = "https://e/", AllowedExtensions = [".folder"] } }
        };
        Worker w = CreateWorker(cfg);
        ExternalConfiguration merged = MergeFor(w, folder, "a.txt");
        merged.AllowedExtensions.Should().Equal(".folder");
    }

    [Fact]
    public void ExcludePatterns_Override_Action_Global_Priority() {
        string folder = Path.Combine(Path.GetTempPath(), "over_all_5");
        var cfg = new ExternalConfiguration {
            ExcludePatterns = ["*_global"],
            Folders = { new ExternalConfiguration.WatchedFolderConfig { FolderPath = folder, ActionName = "rest" } },
            Actions = { new ExternalConfiguration.ActionConfig { Name = "rest", ActionType = ExternalConfiguration.FolderActionType.RestPost, ApiEndpoint = "https://e/", ExcludePatterns = ["*_folder"] } }
        };
        Worker w = CreateWorker(cfg);
        ExternalConfiguration merged = MergeFor(w, folder, "a.txt");
        merged.ExcludePatterns.Should().Equal("*_folder");
    }

    [Fact]
    public void IncludeSubdirectories_Override_Action_Global_Priority() {
        string folder = Path.Combine(Path.GetTempPath(), "over_all_6");
        var cfg = new ExternalConfiguration {
            IncludeSubdirectories = false,
            Folders = { new ExternalConfiguration.WatchedFolderConfig { FolderPath = folder, ActionName = "rest" } },
            Actions = { new ExternalConfiguration.ActionConfig { Name = "rest", ActionType = ExternalConfiguration.FolderActionType.RestPost, ApiEndpoint = "https://e/", IncludeSubdirectories = true } }
        };
        Worker w = CreateWorker(cfg);
        ExternalConfiguration merged = MergeFor(w, folder, "a.txt");
        merged.IncludeSubdirectories.Should().BeTrue();
    }

    [Fact]
    public void DebounceMilliseconds_Override_Action_Global_Priority() {
        string folder = Path.Combine(Path.GetTempPath(), "over_all_7");
        var cfg = new ExternalConfiguration {
            DebounceMilliseconds = 500,
            Folders = { new ExternalConfiguration.WatchedFolderConfig { FolderPath = folder, ActionName = "rest" } },
            Actions = { new ExternalConfiguration.ActionConfig { Name = "rest", ActionType = ExternalConfiguration.FolderActionType.RestPost, ApiEndpoint = "https://e/", DebounceMilliseconds = 111 } }
        };
        Worker w = CreateWorker(cfg);
        ExternalConfiguration merged = MergeFor(w, folder, "a.txt");
        merged.DebounceMilliseconds.Should().Be(111);
    }

    [Fact]
    public void Retries_Override_Action_Global_Priority() {
        string folder = Path.Combine(Path.GetTempPath(), "over_all_8");
        var cfg = new ExternalConfiguration {
            Retries = 3,
            Folders = { new ExternalConfiguration.WatchedFolderConfig { FolderPath = folder, ActionName = "rest" } },
            Actions = { new ExternalConfiguration.ActionConfig { Name = "rest", ActionType = ExternalConfiguration.FolderActionType.RestPost, ApiEndpoint = "https://e/", Retries = 7 } }
        };
        Worker w = CreateWorker(cfg);
        ExternalConfiguration merged = MergeFor(w, folder, "a.txt");
        merged.Retries.Should().Be(7);
    }

    [Fact]
    public void RetryDelayMilliseconds_Override_Action_Global_Priority() {
        string folder = Path.Combine(Path.GetTempPath(), "over_all_9");
        var cfg = new ExternalConfiguration {
            RetryDelayMilliseconds = 100,
            Folders = { new ExternalConfiguration.WatchedFolderConfig { FolderPath = folder, ActionName = "rest" } },
            Actions = { new ExternalConfiguration.ActionConfig { Name = "rest", ActionType = ExternalConfiguration.FolderActionType.RestPost, ApiEndpoint = "https://e/", RetryDelayMilliseconds = 444 } }
        };
        Worker w = CreateWorker(cfg);
        ExternalConfiguration merged = MergeFor(w, folder, "a.txt");
        merged.RetryDelayMilliseconds.Should().Be(444);
    }

    [Fact]
    public void WaitForFileReadyMilliseconds_Override_Action_Global_Priority() {
        string folder = Path.Combine(Path.GetTempPath(), "over_all_10");
        var cfg = new ExternalConfiguration {
            WaitForFileReadyMilliseconds = 500,
            Folders = { new ExternalConfiguration.WatchedFolderConfig { FolderPath = folder, ActionName = "rest" } },
            Actions = { new ExternalConfiguration.ActionConfig { Name = "rest", ActionType = ExternalConfiguration.FolderActionType.RestPost, ApiEndpoint = "https://e/", WaitForFileReadyMilliseconds = 999 } }
        };
        Worker w = CreateWorker(cfg);
        ExternalConfiguration merged = MergeFor(w, folder, "a.txt");
        merged.WaitForFileReadyMilliseconds.Should().Be(999);
    }

    [Fact]
    public void MaxContentBytes_Override_Action_Global_Priority() {
        string folder = Path.Combine(Path.GetTempPath(), "over_all_11");
        var cfg = new ExternalConfiguration {
            MaxContentBytes = 100,
            Folders = { new ExternalConfiguration.WatchedFolderConfig { FolderPath = folder, ActionName = "rest" } },
            Actions = { new ExternalConfiguration.ActionConfig { Name = "rest", ActionType = ExternalConfiguration.FolderActionType.RestPost, ApiEndpoint = "https://e/", MaxContentBytes = 123 } }
        };
        Worker w = CreateWorker(cfg);
        ExternalConfiguration merged = MergeFor(w, folder, "a.txt");
        merged.MaxContentBytes.Should().Be(123);
    }

    [Fact]
    public void StreamingThresholdBytes_Override_Action_Global_Priority() {
        string folder = Path.Combine(Path.GetTempPath(), "over_all_12");
        var cfg = new ExternalConfiguration {
            StreamingThresholdBytes = 10,
            Folders = { new ExternalConfiguration.WatchedFolderConfig { FolderPath = folder, ActionName = "rest" } },
            Actions = { new ExternalConfiguration.ActionConfig { Name = "rest", ActionType = ExternalConfiguration.FolderActionType.RestPost, ApiEndpoint = "https://e/", StreamingThresholdBytes = 20 } }
        };
        Worker w = CreateWorker(cfg);
        ExternalConfiguration merged = MergeFor(w, folder, "a.txt");
        merged.StreamingThresholdBytes.Should().Be(20);
    }

    [Fact]
    public void DiscardZeroByteFiles_Override_Action_Global_Priority() {
        string folder = Path.Combine(Path.GetTempPath(), "over_all_13");
        var cfg = new ExternalConfiguration {
            DiscardZeroByteFiles = false,
            Folders = { new ExternalConfiguration.WatchedFolderConfig { FolderPath = folder, ActionName = "rest" } },
            Actions = { new ExternalConfiguration.ActionConfig { Name = "rest", ActionType = ExternalConfiguration.FolderActionType.RestPost, ApiEndpoint = "https://e/", DiscardZeroByteFiles = true } }
        };
        Worker w = CreateWorker(cfg);
        ExternalConfiguration merged = MergeFor(w, folder, "a.txt");
        merged.DiscardZeroByteFiles.Should().BeTrue();
    }

    [Fact]
    public void EnableCircuitBreaker_Override_Action_Global_Priority() {
        string folder = Path.Combine(Path.GetTempPath(), "over_all_14");
        var cfg = new ExternalConfiguration {
            EnableCircuitBreaker = false,
            Folders = { new ExternalConfiguration.WatchedFolderConfig { FolderPath = folder, ActionName = "rest" } },
            Actions = { new ExternalConfiguration.ActionConfig { Name = "rest", ActionType = ExternalConfiguration.FolderActionType.RestPost, ApiEndpoint = "https://e/", EnableCircuitBreaker = true } }
        };
        Worker w = CreateWorker(cfg);
        ExternalConfiguration merged = MergeFor(w, folder, "a.txt");
        merged.EnableCircuitBreaker.Should().BeTrue();
    }

    [Fact]
    public void CircuitBreakerFailureThreshold_Override_Action_Global_Priority() {
        string folder = Path.Combine(Path.GetTempPath(), "over_all_15");
        var cfg = new ExternalConfiguration {
            CircuitBreakerFailureThreshold = 5,
            Folders = { new ExternalConfiguration.WatchedFolderConfig { FolderPath = folder, ActionName = "rest" } },
            Actions = { new ExternalConfiguration.ActionConfig { Name = "rest", ActionType = ExternalConfiguration.FolderActionType.RestPost, ApiEndpoint = "https://e/", CircuitBreakerFailureThreshold = 7 } }
        };
        var w = CreateWorker(cfg);
        var merged = MergeFor(w, folder, "a.txt");
        merged.CircuitBreakerFailureThreshold.Should().Be(7);
    }

    [Fact]
    public void CircuitBreakerOpenDurationMilliseconds_Override_Action_Global_Priority() {
        string folder = Path.Combine(Path.GetTempPath(), "over_all_16");
        var cfg = new ExternalConfiguration {
            CircuitBreakerOpenDurationMilliseconds = 1000,
            Folders = { new ExternalConfiguration.WatchedFolderConfig { FolderPath = folder, ActionName = "rest" } },
            Actions = { new ExternalConfiguration.ActionConfig { Name = "rest", ActionType = ExternalConfiguration.FolderActionType.RestPost, ApiEndpoint = "https://e/", CircuitBreakerOpenDurationMilliseconds = 777 } }
        };
        var w = CreateWorker(cfg);
        var merged = MergeFor(w, folder, "a.txt");
        merged.CircuitBreakerOpenDurationMilliseconds.Should().Be(777);
    }
}

// Reuse mocks from OverridesBehaviorTests in the same namespace
