namespace FileWatchRest.Tests;

public class WorkerRetryTests {
    private readonly ILogger<Worker> _workerLogger;
    private readonly ILogger<DiagnosticsService> _diagLogger;

    public WorkerRetryTests() {
        ILoggerFactory factory = LoggerFactory.Create(builder => builder.AddDebug().SetMinimumLevel(LogLevel.Debug));
        _workerLogger = factory.CreateLogger<Worker>();
        _diagLogger = factory.CreateLogger<DiagnosticsService>();
    }

    [Fact]
    public async Task CreateNotificationAsyncRespectsMaxContentBytes() {
        string temp = Path.GetTempFileName();
        try {
            // Write > 10 bytes
            await File.WriteAllTextAsync(temp, new string('A', 100));

            var handler = new TestHttpMessageHandler(HttpStatusCode.OK);
            var httpClientFactory = new CustomHttpClientFactory(handler);

            var lifetime = new TestHostApplicationLifetime();
            var diagnostics = new DiagnosticsService(_diagLogger, new OptionsMonitorMock<ExternalConfiguration>());
            string configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "FileWatchRest_Test_CreateNotification", "FileWatchRest.json");
            var watcherManager = new FileWatcherManager(LoggerFactory.Create(builder => builder.AddDebug()).CreateLogger<FileWatcherManager>(), diagnostics);
            var resilience = new HttpResilienceService(LoggerFactory.Create(builder => builder.AddDebug()).CreateLogger<HttpResilienceService>(), diagnostics);
            var initialConfig = new ExternalConfiguration();
            var optionsMonitor = new SimpleOptionsMonitor<ExternalConfiguration>(initialConfig);

            Worker worker = WorkerFactory.CreateWorker(logger: _workerLogger, httpClientFactory: httpClientFactory, lifetime: lifetime, diagnostics: diagnostics, fileWatcherManager: watcherManager, resilienceService: resilience, optionsMonitor: optionsMonitor);
            worker.CurrentConfig = new ExternalConfiguration { PostFileContents = true, MaxContentBytes = 10 };

            FileNotification notification = await worker.CreateNotificationAsync(temp, CancellationToken.None);
            notification.Content.Should().BeNull();
        }
        finally {
            try { File.Delete(temp); } catch { }
        }
    }

    [Fact]
    public async Task SendNotificationAsyncRetriesUntilSuccess() {
        var handler = new TestHttpMessageHandler(HttpStatusCode.InternalServerError, HttpStatusCode.InternalServerError, HttpStatusCode.OK);
        var httpClientFactory = new CustomHttpClientFactory(handler);

        var lifetime = new TestHostApplicationLifetime();
        var diagnostics = new DiagnosticsService(_diagLogger, new OptionsMonitorMock<ExternalConfiguration>());
        string configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "FileWatchRest_Test_Retry", "FileWatchRest.json");
        var watcherManager = new FileWatcherManager(LoggerFactory.Create(builder => builder.AddDebug()).CreateLogger<FileWatcherManager>(), diagnostics);
        var resilience = new HttpResilienceService(LoggerFactory.Create(builder => builder.AddDebug()).CreateLogger<HttpResilienceService>(), diagnostics);
        var initial2 = new ExternalConfiguration();
        var optionsMonitor2 = new SimpleOptionsMonitor<ExternalConfiguration>(initial2);
        Worker worker = WorkerFactory.CreateWorker(logger: _workerLogger, httpClientFactory: httpClientFactory, lifetime: lifetime, diagnostics: diagnostics, fileWatcherManager: watcherManager, resilienceService: resilience, optionsMonitor: optionsMonitor2);

        worker.CurrentConfig = new ExternalConfiguration {
            ApiEndpoint = "http://localhost/webhook",
            Retries = 2,
            RetryDelayMilliseconds = 10,
            PostFileContents = false
        };

        var notification = new FileNotification { Path = "test" };
        bool result = await worker.SendNotificationAsync(notification, CancellationToken.None);

        result.Should().BeTrue();
        handler.AttemptCount.Should().Be(3);
    }

    [Fact]
    public async Task CircuitBreakerOpensAfterThresholdAndShortCircuits() {
        var handler = new TestHttpMessageHandler(HttpStatusCode.InternalServerError, HttpStatusCode.InternalServerError, HttpStatusCode.InternalServerError);
        var httpClientFactory = new CustomHttpClientFactory(handler);

        var lifetime = new TestHostApplicationLifetime();
        var diagnostics = new DiagnosticsService(_diagLogger, new OptionsMonitorMock<ExternalConfiguration>());
        string configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "FileWatchRest_Test_Circuit", "FileWatchRest.json");
        var watcherManager = new FileWatcherManager(LoggerFactory.Create(builder => builder.AddDebug()).CreateLogger<FileWatcherManager>(), diagnostics);
        var resilience = new HttpResilienceService(LoggerFactory.Create(builder => builder.AddDebug()).CreateLogger<HttpResilienceService>(), diagnostics);
        var initial3 = new ExternalConfiguration();
        var optionsMonitor3 = new SimpleOptionsMonitor<ExternalConfiguration>(initial3);
        Worker worker = WorkerFactory.CreateWorker(logger: _workerLogger, httpClientFactory: httpClientFactory, lifetime: lifetime, diagnostics: diagnostics, fileWatcherManager: watcherManager, resilienceService: resilience, optionsMonitor: optionsMonitor3);

        worker.CurrentConfig = new ExternalConfiguration {
            ApiEndpoint = "http://localhost/webhook",
            Retries = 0, // single attempt per Send
            RetryDelayMilliseconds = 1,
            PostFileContents = false,
            EnableCircuitBreaker = true,
            CircuitBreakerFailureThreshold = 2,
            CircuitBreakerOpenDurationMilliseconds = 10000
        };

        var notification = new FileNotification { Path = "test" };

        // First call -> fails (handler.AttemptCount++ -> 1)
        bool r1 = await worker.SendNotificationAsync(notification, CancellationToken.None);
        r1.Should().BeFalse();
        handler.AttemptCount.Should().Be(1);

        // Second call -> fails and opens circuit (AttemptCount -> 2)
        bool r2 = await worker.SendNotificationAsync(notification, CancellationToken.None);
        r2.Should().BeFalse();
        handler.AttemptCount.Should().Be(2);

        // Third call -> circuit open -> short-circuit, no more HTTP calls
        bool r3 = await worker.SendNotificationAsync(notification, CancellationToken.None);
        r3.Should().BeFalse();
        handler.AttemptCount.Should().Be(2);
    }

    [Fact]
    public async Task SendNotificationAsyncSetsAuthorizationHeader() {
        var handler = new TestHttpMessageHandler(HttpStatusCode.OK);
        var httpClientFactory = new CustomHttpClientFactory(handler);

        var lifetime = new TestHostApplicationLifetime();
        var diagnostics = new DiagnosticsService(_diagLogger, new OptionsMonitorMock<ExternalConfiguration>());
        string configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "FileWatchRest_Test_Auth", "FileWatchRest.json");
        var watcherManager = new FileWatcherManager(LoggerFactory.Create(builder => builder.AddDebug()).CreateLogger<FileWatcherManager>(), diagnostics);
        var resilience = new HttpResilienceService(LoggerFactory.Create(builder => builder.AddDebug()).CreateLogger<HttpResilienceService>(), diagnostics);
        var initial4 = new ExternalConfiguration();
        var optionsMonitor4 = new SimpleOptionsMonitor<ExternalConfiguration>(initial4);
        Worker worker = WorkerFactory.CreateWorker(logger: _workerLogger, httpClientFactory: httpClientFactory, lifetime: lifetime, diagnostics: diagnostics, fileWatcherManager: watcherManager, resilienceService: resilience, optionsMonitor: optionsMonitor4);

        worker.CurrentConfig = new ExternalConfiguration {
            ApiEndpoint = "http://localhost/webhook",
            Retries = 0,
            RetryDelayMilliseconds = 1,
            PostFileContents = false,
            BearerToken = "test-token-xyz"
        };

        var notification = new FileNotification { Path = "test" };

        bool result = await worker.SendNotificationAsync(notification, CancellationToken.None);

        result.Should().BeTrue();
        handler.LastAuthorizationHeader.Should().Be("Bearer test-token-xyz");
    }

    // Lightweight test HTTP handler that returns a sequence of status codes and records attempt count
    private sealed class TestHttpMessageHandler(params HttpStatusCode[] responses) : HttpMessageHandler {
        private readonly HttpStatusCode[] _responses = responses.Length > 0 ? responses : [HttpStatusCode.OK];
        private int _index;
        public int AttemptCount => _index;
        public string? LastAuthorizationHeader { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            // Capture Authorization header for test assertions
            LastAuthorizationHeader = request.Headers.Authorization?.ToString();

            int idx = Math.Min(_index, _responses.Length - 1);
            HttpStatusCode status = _responses[idx];
            Interlocked.Increment(ref _index);
            var content = new StringContent("{}", Encoding.UTF8);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            var response = new HttpResponseMessage(status) { Content = content };
            return Task.FromResult(response);
        }
    }

    private sealed class CustomHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory {
        private readonly HttpMessageHandler _handler = handler;

        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }
}
