namespace FileWatchRest.Tests;

public class WorkerHelperTests {
    [Fact]
    public void ShouldUseStreamingUploadBehavesAsExpected() {
        ILoggerFactory factory = LoggerFactory.Create(b => b.AddDebug());
        ILogger<Worker> workerLogger = factory.CreateLogger<Worker>();
        ILogger<DiagnosticsService> diagLogger = factory.CreateLogger<DiagnosticsService>();
        ILogger<FileWatcherManager> watcherLogger = factory.CreateLogger<FileWatcherManager>();

        var lifetime = new TestHostApplicationLifetime();
        var diagnostics = new DiagnosticsService(diagLogger, new OptionsMonitorMock<ExternalConfiguration>());
        var watcherManager = new FileWatcherManager(watcherLogger, diagnostics);
        var resilience = new HttpResilienceService(factory.CreateLogger<HttpResilienceService>(), diagnostics);
        var options = new SimpleOptionsMonitor<ExternalConfiguration>(new ExternalConfiguration());
        var httpClientFactory = new TestHttpClientFactory();

        Worker worker = WorkerFactory.CreateWorker(
            logger: workerLogger,
            httpClientFactory: httpClientFactory,
            lifetime: lifetime,
            diagnostics: diagnostics,
            fileWatcherManager: watcherManager,
            resilienceService: resilience,
            optionsMonitor: options);

        worker.CurrentConfig = new ExternalConfiguration {
            PostFileContents = true,
            StreamingThresholdBytes = 100,
            MaxContentBytes = 10_000
        };

        var small = new FileNotification { FileSize = 50, Path = "small" };
        var large = new FileNotification { FileSize = 1024, Path = "large" };

        worker.ShouldUseStreamingUpload(small).Should().BeFalse();
        worker.ShouldUseStreamingUpload(large).Should().BeTrue();
    }
}
