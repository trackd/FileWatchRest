namespace FileWatchRest.Tests.Helpers;

/// <summary>
/// Factory for creating Worker instances with proper test dependencies.
/// </summary>
public static class WorkerFactory {
    /// <summary>
    /// Creates a Worker instance with all required dependencies for testing.
    /// </summary>
    public static Worker CreateWorker(
        ExternalConfiguration? config = null,
        ILogger<Worker>? logger = null,
        IHttpClientFactory? httpClientFactory = null,
        IHostApplicationLifetime? lifetime = null,
        DiagnosticsService? diagnostics = null,
        FileWatcherManager? fileWatcherManager = null,
        FileDebounceService? debounceService = null,
        IResilienceService? resilienceService = null,
        IOptionsMonitor<ExternalConfiguration>? optionsMonitor = null) {
        // Use defaults if not provided
        config ??= new ExternalConfiguration();
        logger ??= NullLogger<Worker>.Instance;
        httpClientFactory ??= new HttpClientFactoryMock();
        lifetime ??= new HostApplicationLifetimeMock();

        // Create diagnostics if not provided
        diagnostics ??= new DiagnosticsService(
            NullLogger<DiagnosticsService>.Instance,
            optionsMonitor ?? new SimpleOptionsMonitor<ExternalConfiguration>(config));

        // Create file watcher manager if not provided
        fileWatcherManager ??= new FileWatcherManager(
            NullLogger<FileWatcherManager>.Instance,
            diagnostics);

        // Create debounce service if not provided
        debounceService ??= new FileDebounceServiceMock();

        // Create resilience service if not provided
        resilienceService ??= new ResilienceServiceMock();

        // Create options monitor if not provided
        optionsMonitor ??= new SimpleOptionsMonitor<ExternalConfiguration>(config);

        return new Worker(
            logger,
            httpClientFactory,
            lifetime,
            diagnostics,
            fileWatcherManager,
            debounceService,
            resilienceService,
            optionsMonitor);
    }
}
