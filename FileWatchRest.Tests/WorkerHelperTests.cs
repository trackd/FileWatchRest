namespace FileWatchRest.Tests;

public class WorkerHelperTests
{
    [Fact]
    public void ShouldUseStreamingUpload_BehavesAsExpected()
    {
        var factory = LoggerFactory.Create(b => b.AddDebug());
        var workerLogger = factory.CreateLogger<Worker>();
        var diagLogger = factory.CreateLogger<DiagnosticsService>();
        var configLogger = factory.CreateLogger<ConfigurationService>();
        var watcherLogger = factory.CreateLogger<FileWatcherManager>();

        var lifetime = new TestHostApplicationLifetime();
        var diagnostics = new DiagnosticsService(diagLogger);
        var configService = new ConfigurationService(configLogger, "FileWatchRest_Test_Helper");
        var watcherManager = new FileWatcherManager(watcherLogger, diagnostics);
        var resilience = new HttpResilienceService(factory.CreateLogger<HttpResilienceService>(), diagnostics);
        var options = new SimpleOptionsMonitor<ExternalConfiguration>(new ExternalConfiguration());
        var httpClientFactory = new TestHttpClientFactory();

        var worker = new Worker(workerLogger, httpClientFactory, lifetime, diagnostics, configService, watcherManager, resilience, options);

        worker.CurrentConfig = new ExternalConfiguration
        {
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
