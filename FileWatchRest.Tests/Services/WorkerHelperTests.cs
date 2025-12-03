namespace FileWatchRest.Tests.Services;

internal sealed class TestOptionsMonitor : IOptionsMonitor<ExternalConfiguration> {
    public ExternalConfiguration CurrentValue { get; set; } = new ExternalConfiguration();
    public ExternalConfiguration Get(string? name) => CurrentValue;
    public IDisposable OnChange(Action<ExternalConfiguration, string> listener) => new DummyDisposable();
    private sealed class DummyDisposable : IDisposable { public void Dispose() { } }
}

internal sealed class DummyHostLifetime : IHostApplicationLifetime {
    public CancellationToken ApplicationStarted => CancellationToken.None;
    public CancellationToken ApplicationStopping => CancellationToken.None;
    public CancellationToken ApplicationStopped => CancellationToken.None;
    public void StopApplication() { }
}

internal sealed class DummyResilience : IResilienceService {
    public Task<ResilienceResult> SendWithRetriesAsync(Func<CancellationToken, Task<HttpRequestMessage>> requestFactory, HttpClient client, string endpointKey, ExternalConfiguration cfg, CancellationToken ct) =>
        Task.FromResult(new ResilienceResult(true, 1, 200, null, 1, false));
}

internal sealed class SimpleHttpClientFactory : IHttpClientFactory {
    public HttpClient CreateClient(string name) => new();
}

public class WorkerHelperTests {
    [Fact]
    public async Task WaitForFileReadyAsync_file_readable_returns_true() {
        string tmp = Path.GetTempFileName();
        await File.WriteAllTextAsync(tmp, "hello");
        var cfg = new ExternalConfiguration { WaitForFileReadyMilliseconds = 500, PostFileContents = false };

        bool ready = await Worker.WaitForFileReadyAsync(tmp, cfg, CancellationToken.None);
        ready.Should().BeTrue();

        try { File.Delete(tmp); } catch { }
    }

    [Fact]
    public async Task WaitForFileReadyAsync_empty_file_discard_true_returns_false() {
        string tmp = Path.GetTempFileName();
        // leave empty
        var cfg = new ExternalConfiguration { WaitForFileReadyMilliseconds = 200, PostFileContents = true, DiscardZeroByteFiles = true };

        bool ready = await Worker.WaitForFileReadyAsync(tmp, cfg, CancellationToken.None);
        ready.Should().BeFalse();

        try { File.Delete(tmp); } catch { }
    }

    [Fact]
    public async Task CreateNotificationAsync_reads_content_when_configured() {
        string tmp = Path.GetTempFileName();
        await File.WriteAllTextAsync(tmp, "content123");

        var options = new TestOptionsMonitor();
        var diagnostics = new DiagnosticsService(NullLogger<DiagnosticsService>.Instance, options);
        var fw = new FileWatcherManager(NullLogger<FileWatcherManager>.Instance, diagnostics);
        var debounce = new FileDebounceService(NullLogger<FileDebounceService>.Instance, Channel.CreateUnbounded<string>().Writer, () => new ExternalConfiguration());
        var worker = new Worker(NullLogger<Worker>.Instance, new SimpleHttpClientFactory(), new DummyHostLifetime(), diagnostics, fw, debounce, new DummyResilience(), options);

        var cfg = new ExternalConfiguration { PostFileContents = true, MaxContentBytes = 1024 * 10 };
        FileNotification notification = await worker.CreateNotificationAsync(tmp, cfg, CancellationToken.None);
        notification.Content.Should().Contain("content123");

        try { File.Delete(tmp); } catch { }
    }

    [Fact]
    public void ShouldUseStreamingUpload_behaviour() {
        var cfg = new ExternalConfiguration { PostFileContents = true, StreamingThresholdBytes = 100, MaxContentBytes = 1024 };
        var n = new FileNotification { Path = "a", FileSize = 200 };
        Worker.ShouldUseStreamingUpload(n, cfg).Should().BeTrue();

        n.FileSize = 50;
        Worker.ShouldUseStreamingUpload(n, cfg).Should().BeFalse();
    }
}
