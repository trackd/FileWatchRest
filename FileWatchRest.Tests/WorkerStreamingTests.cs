namespace FileWatchRest.Tests;

public class WorkerStreamingTests
{
    [Fact(DisplayName = "SendNotificationAsync_FileLargerThanThreshold_UsesMultipartContent")]
    public async Task SendNotificationAsync_FileLargerThanThreshold_UsesMultipartContent()
    {
        var loggerFactory = LoggerFactory.Create(b => b.AddDebug().SetMinimumLevel(LogLevel.Debug));
        var workerLogger = loggerFactory.CreateLogger<Worker>();
        var diagLogger = loggerFactory.CreateLogger<DiagnosticsService>();
        var configLogger = loggerFactory.CreateLogger<ConfigurationService>();
        var watcherLogger = loggerFactory.CreateLogger<FileWatcherManager>();

        var lifetime = new TestHostApplicationLifetime();
        var diagnostics = new DiagnosticsService(diagLogger);
        var configService = new ConfigurationService(configLogger, "FileWatchRest_Test_Stream");
        var watcherManager = new FileWatcherManager(watcherLogger, diagnostics);
        var httpClientFactory = new TestHttpClientFactory();

        var initial = new ExternalConfiguration();
        var optionsMonitor = new SimpleOptionsMonitor<ExternalConfiguration>(initial);

        var testResilience = new CapturingResilienceService();

        var worker = new Worker(workerLogger, httpClientFactory, lifetime, diagnostics, configService, watcherManager, testResilience, optionsMonitor);

        // Configure to use streaming for files > 100 bytes and allow up to 10KB
        worker.CurrentConfig = new ExternalConfiguration
        {
            ApiEndpoint = "http://localhost/api/files",
            PostFileContents = true,
            StreamingThresholdBytes = 100,
            MaxContentBytes = 10 * 1024
        };

        // Create a temporary file slightly larger than the threshold
        var tempPath = Path.Combine(Path.GetTempPath(), $"fwrest_test_{Guid.NewGuid():N}.bin");
        try
        {
            var size = 512; // > 100, < MaxContentBytes
            var buffer = new byte[size];
            Random.Shared.NextBytes(buffer);
            await File.WriteAllBytesAsync(tempPath, buffer);

            var notification = new FileNotification
            {
                Path = tempPath,
                FileSize = size,
                LastWriteTime = DateTime.Now
            };

            var result = await worker.SendNotificationAsync(notification, CancellationToken.None);

            result.Should().BeTrue();
            testResilience.LastRequest.Should().NotBeNull();
            testResilience.LastRequestContentType.Should().Be(typeof(MultipartFormDataContent));
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
        }
    }

        private sealed class CapturingResilienceService : IResilienceService
    {
    public HttpRequestMessage? LastRequest { get; private set; }
    public Type? LastRequestContentType { get; private set; }

        public async Task<ResilienceResult> SendWithRetriesAsync(Func<CancellationToken, Task<HttpRequestMessage>> requestFactory, HttpClient client, string endpointKey, ExternalConfiguration config, CancellationToken ct)
        {
            var req = await requestFactory(ct).ConfigureAwait(false);
            // Capture a shallow copy of the request metadata for assertions; do not hold onto the request which may own streams.
            LastRequestContentType = req.Content?.GetType();
            LastRequest = new HttpRequestMessage(req.Method, req.RequestUri)
            {
                Version = req.Version
            };
            foreach (var h in req.Headers) LastRequest.Headers.TryAddWithoutValidation(h.Key, h.Value);
            return new ResilienceResult(true, 1, 200, null, 0, false);
        }
    }

    private class TestHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name = "") => new HttpClient();
    }
}
