namespace FileWatchRest.Tests;

public class WorkerRetryTests
{
    private readonly ILogger<Worker> _workerLogger;
    private readonly ILogger<ConfigurationService> _configLogger;
    private readonly ILogger<DiagnosticsService> _diagLogger;

    public WorkerRetryTests()
    {
        var factory = LoggerFactory.Create(builder => builder.AddDebug().SetMinimumLevel(LogLevel.Debug));
        _workerLogger = factory.CreateLogger<Worker>();
        _configLogger = factory.CreateLogger<ConfigurationService>();
        _diagLogger = factory.CreateLogger<DiagnosticsService>();
    }

    [Fact]
    public async Task CreateNotificationAsync_Respects_MaxContentBytes()
    {
        var temp = Path.GetTempFileName();
        try
        {
            // Write > 10 bytes
            await File.WriteAllTextAsync(temp, new string('A', 100));

            var handler = new TestHttpMessageHandler(HttpStatusCode.OK);
            var httpClientFactory = new CustomHttpClientFactory(handler);

            var lifetime = new TestHostApplicationLifetime();
            var diagnostics = new DiagnosticsService(_diagLogger);
            var configService = new ConfigurationService(_configLogger, "FileWatchRest_Test_CreateNotification");
            var watcherManager = new FileWatcherManager(LoggerFactory.Create(builder => builder.AddDebug()).CreateLogger<FileWatcherManager>(), diagnostics);
            var resilience = new HttpResilienceService(LoggerFactory.Create(builder => builder.AddDebug()).CreateLogger<HttpResilienceService>(), diagnostics);
            var initialConfig = await configService.LoadConfigurationAsync();
            var optionsMonitor = new SimpleOptionsMonitor<ExternalConfiguration>(initialConfig);

            var worker = new Worker(_workerLogger, httpClientFactory, lifetime, diagnostics, configService, watcherManager, resilience, optionsMonitor);
            worker.CurrentConfig = new ExternalConfiguration { PostFileContents = true, MaxContentBytes = 10 };

            var notification = await worker.CreateNotificationAsync(temp, CancellationToken.None);
            notification.Content.Should().BeNull();
        }
        finally
        {
            try { File.Delete(temp); } catch { }
        }
    }

    [Fact]
    public async Task SendNotificationAsync_Retries_Until_Success()
    {
        var handler = new TestHttpMessageHandler(HttpStatusCode.InternalServerError, HttpStatusCode.InternalServerError, HttpStatusCode.OK);
        var httpClientFactory = new CustomHttpClientFactory(handler);

        var lifetime = new TestHostApplicationLifetime();
        var diagnostics = new DiagnosticsService(_diagLogger);
        var configService = new ConfigurationService(_configLogger, "FileWatchRest_Test_Retry");
        var watcherManager = new FileWatcherManager(LoggerFactory.Create(builder => builder.AddDebug()).CreateLogger<FileWatcherManager>(), diagnostics);
        var resilience = new HttpResilienceService(LoggerFactory.Create(builder => builder.AddDebug()).CreateLogger<HttpResilienceService>(), diagnostics);
        var initial2 = await configService.LoadConfigurationAsync();
        var optionsMonitor2 = new SimpleOptionsMonitor<ExternalConfiguration>(initial2);
        var worker = new Worker(_workerLogger, httpClientFactory, lifetime, diagnostics, configService, watcherManager, resilience, optionsMonitor2);

        worker.CurrentConfig = new ExternalConfiguration
        {
            ApiEndpoint = "http://localhost/webhook",
            Retries = 2,
            RetryDelayMilliseconds = 10,
            PostFileContents = false
        };

        var notification = new FileNotification { Path = "test" };
        var result = await worker.SendNotificationAsync(notification, CancellationToken.None);

        result.Should().BeTrue();
        handler.AttemptCount.Should().Be(3);
    }

    [Fact]
    public async Task CircuitBreaker_Opens_After_Threshold_And_ShortCircuits()
    {
        var handler = new TestHttpMessageHandler(HttpStatusCode.InternalServerError, HttpStatusCode.InternalServerError, HttpStatusCode.InternalServerError);
        var httpClientFactory = new CustomHttpClientFactory(handler);

        var lifetime = new TestHostApplicationLifetime();
        var diagnostics = new DiagnosticsService(_diagLogger);
        var configService = new ConfigurationService(_configLogger, "FileWatchRest_Test_Circuit");
        var watcherManager = new FileWatcherManager(LoggerFactory.Create(builder => builder.AddDebug()).CreateLogger<FileWatcherManager>(), diagnostics);
        var resilience = new HttpResilienceService(LoggerFactory.Create(builder => builder.AddDebug()).CreateLogger<HttpResilienceService>(), diagnostics);
        var initial3 = await configService.LoadConfigurationAsync();
        var optionsMonitor3 = new SimpleOptionsMonitor<ExternalConfiguration>(initial3);
        var worker = new Worker(_workerLogger, httpClientFactory, lifetime, diagnostics, configService, watcherManager, resilience, optionsMonitor3);

        worker.CurrentConfig = new ExternalConfiguration
        {
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
        var r1 = await worker.SendNotificationAsync(notification, CancellationToken.None);
        r1.Should().BeFalse();
        handler.AttemptCount.Should().Be(1);

        // Second call -> fails and opens circuit (AttemptCount -> 2)
        var r2 = await worker.SendNotificationAsync(notification, CancellationToken.None);
        r2.Should().BeFalse();
        handler.AttemptCount.Should().Be(2);

        // Third call -> circuit open -> short-circuit, no more HTTP calls
        var r3 = await worker.SendNotificationAsync(notification, CancellationToken.None);
        r3.Should().BeFalse();
        handler.AttemptCount.Should().Be(2);
    }

    [Fact]
    public async Task SendNotificationAsync_Sets_Authorization_Header()
    {
        var handler = new TestHttpMessageHandler(HttpStatusCode.OK);
        var httpClientFactory = new CustomHttpClientFactory(handler);

        var lifetime = new TestHostApplicationLifetime();
        var diagnostics = new DiagnosticsService(_diagLogger);
        var configService = new ConfigurationService(_configLogger, "FileWatchRest_Test_Auth");
        var watcherManager = new FileWatcherManager(LoggerFactory.Create(builder => builder.AddDebug()).CreateLogger<FileWatcherManager>(), diagnostics);
        var resilience = new HttpResilienceService(LoggerFactory.Create(builder => builder.AddDebug()).CreateLogger<HttpResilienceService>(), diagnostics);
        var initial4 = await configService.LoadConfigurationAsync();
        var optionsMonitor4 = new SimpleOptionsMonitor<ExternalConfiguration>(initial4);
        var worker = new Worker(_workerLogger, httpClientFactory, lifetime, diagnostics, configService, watcherManager, resilience, optionsMonitor4);

        worker.CurrentConfig = new ExternalConfiguration
        {
            ApiEndpoint = "http://localhost/webhook",
            Retries = 0,
            RetryDelayMilliseconds = 1,
            PostFileContents = false,
            BearerToken = "test-token-xyz"
        };

        var notification = new FileNotification { Path = "test" };

        var result = await worker.SendNotificationAsync(notification, CancellationToken.None);

        result.Should().BeTrue();
        handler.LastAuthorizationHeader.Should().Be("Bearer test-token-xyz");
    }

    // Lightweight test HTTP handler that returns a sequence of status codes and records attempt count
    private class TestHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode[] _responses;
        private int _index = 0;
        public int AttemptCount => _index;
        public string? LastAuthorizationHeader { get; private set; }

        public TestHttpMessageHandler(params HttpStatusCode[] responses)
        {
            _responses = responses.Length > 0 ? responses : new[] { HttpStatusCode.OK };
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // Capture Authorization header for test assertions
            LastAuthorizationHeader = request.Headers.Authorization?.ToString();

            var idx = Math.Min(_index, _responses.Length - 1);
            var status = _responses[idx];
            Interlocked.Increment(ref _index);
            var content = new StringContent("{}", System.Text.Encoding.UTF8);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            var response = new HttpResponseMessage(status) { Content = content };
            return Task.FromResult(response);
        }
    }

    private class CustomHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public CustomHttpClientFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name)
        {
            return new HttpClient(_handler, disposeHandler: false);
        }
    }
}
