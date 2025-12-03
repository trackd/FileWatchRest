namespace FileWatchRest.Tests;

public class WorkerDuplicatePreventionTests {
    [Fact]
    public async Task SenderLoopAsyncSkipsAlreadyPostedFiles() {
        ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.AddDebug().SetMinimumLevel(LogLevel.Debug));
        ILogger<Worker> logger = loggerFactory.CreateLogger<Worker>();
        var mockHttpFactory = new Mock<IHttpClientFactory>();
        var mockLifetime = new Mock<IHostApplicationLifetime>();
        var diagnostics = new DiagnosticsService(loggerFactory.CreateLogger<DiagnosticsService>(), new OptionsMonitorMock<ExternalConfiguration>());
        var fileWatcherManager = new FileWatcherManager(loggerFactory.CreateLogger<FileWatcherManager>(), diagnostics);
        IResilienceService resilienceService = new Mock<IResilienceService>().Object;
        var optionsMonitor = new OptionsMonitorMock<ExternalConfiguration>();

        Worker worker = WorkerFactory.CreateWorker(
            logger: logger,
            httpClientFactory: mockHttpFactory.Object,
            lifetime: mockLifetime.Object,
            diagnostics: diagnostics,
            fileWatcherManager: fileWatcherManager,
            resilienceService: resilienceService,
            optionsMonitor: optionsMonitor
        );

        // Simulate file posted
        string testPath = "C:/test/file.txt";
        diagnostics.RecordFileEvent(testPath, true, 200);

        // Run ProcessFileAsync using reflection - if file is already posted, it should be skipped (no exception)
        CancellationToken ct = new CancellationTokenSource(1000).Token;
        MethodInfo? processFileMethod = typeof(Worker).GetMethod("ProcessFileAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(processFileMethod);
        object? result = processFileMethod.Invoke(worker, [testPath, ct]);
        Assert.NotNull(result);

        // If it's a ValueTask, convert it to Task
        if (result is ValueTask valueTask) {
            await valueTask.AsTask();
        }
        else {
            await (Task)result;
        }

        // If file is already posted, it should be skipped (no exception)
    }
}
