namespace FileWatchRest.Tests;

public class WorkerEnqueueExistingFilesTests {
    [Fact]
    public async Task EnqueueExistingFilesAsyncSkipsAlreadyPostedFiles() {
        ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.AddDebug().SetMinimumLevel(LogLevel.Debug));
        ILogger<Worker> logger = loggerFactory.CreateLogger<Worker>();
        var mockHttpFactory = new Mock<IHttpClientFactory>();
        var mockLifetime = new Mock<IHostApplicationLifetime>();
        var diagnostics = new DiagnosticsService(loggerFactory.CreateLogger<DiagnosticsService>(), new TestUtilities.OptionsMonitorMock<ExternalConfiguration>());
        var fileWatcherManager = new FileWatcherManager(loggerFactory.CreateLogger<FileWatcherManager>(), diagnostics);
        IResilienceService resilienceService = new Mock<IResilienceService>().Object;
        var optionsMonitor = new TestUtilities.OptionsMonitorMock<ExternalConfiguration>();

        Worker worker = WorkerFactory.CreateWorker(
            logger: logger,
            httpClientFactory: mockHttpFactory.Object,
            lifetime: mockLifetime.Object,
            diagnostics: diagnostics,
            fileWatcherManager: fileWatcherManager,
            resilienceService: resilienceService,
            optionsMonitor: optionsMonitor
        );

        // Simulate already posted file
        string testPath = Path.GetTempFileName();
        diagnostics.RecordFileEvent(testPath, true, 200);

        // Create a folder and add the file
        string testDir = Path.GetDirectoryName(testPath)!;
        var folders = new List<ExternalConfiguration.WatchedFolderConfig>
            {
                new() { FolderPath = testDir }
            };
        var config = new ExternalConfiguration {
            AllowedExtensions = [Path.GetExtension(testPath)],
            DebounceMilliseconds = 0,
            // Set normalized folders list
            Folders = folders
        };
        worker.CurrentConfig = config;

        // Get the debounce service mock from the worker to check what was scheduled
        System.Reflection.FieldInfo? debounceServiceField = typeof(Worker).GetField("_debounceService", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(debounceServiceField);
        var debounceService = debounceServiceField.GetValue(worker) as FileDebounceServiceMock;
        Assert.NotNull(debounceService);
        debounceService.ClearScheduled();

        // Run EnqueueExistingFilesAsync
        System.Reflection.MethodInfo? enqueueMethod = typeof(Worker).GetMethod("EnqueueExistingFilesAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(enqueueMethod);
        object? result = enqueueMethod.Invoke(worker, [CancellationToken.None]);
        Assert.NotNull(result);
        await (Task)result;

        // Assert: file should not be scheduled (because it was already posted)
        Assert.DoesNotContain(testPath, debounceService.ScheduledPaths);

        // Cleanup
        File.Delete(testPath);
    }
}
