namespace FileWatchRest.Tests.Services;

public class HttpResilienceServiceTests {
    private sealed class StaticHandler(HttpResponseMessage response) : HttpMessageHandler {
        private readonly HttpResponseMessage _response = response;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(_response.StatusCode) { Content = _response.Content });
    }

    [Fact]
    public async Task SendWithRetriesAsync_success_returns_success() {
        NullLogger<HttpResilienceService> logger = NullLogger<HttpResilienceService>.Instance;
        NullLogger<DiagnosticsService> diagLogger = NullLogger<DiagnosticsService>.Instance;
        var cfgMonitor = new FileWatchRest.TestUtilities.OptionsMonitorMock<ExternalConfiguration>();
        var diag = new DiagnosticsService(diagLogger, cfgMonitor);

        var handler = new StaticHandler(new HttpResponseMessage(HttpStatusCode.OK));
        var client = new HttpClient(handler);

        var svc = new HttpResilienceService(logger, diag);

        ResilienceResult result = await svc.SendWithRetriesAsync(ct => Task.FromResult(new HttpRequestMessage(HttpMethod.Get, "http://example/")), client, "ep", new ExternalConfiguration { Retries = 0, RetryDelayMilliseconds = 100 }, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Attempts.Should().Be(1);
        result.LastStatusCode.Should().Be(200);
        result.ShortCircuited.Should().BeFalse();
    }

    [Fact]
    public async Task SendWithRetriesAsync_failure_opens_circuit_and_short_circuits_next_call() {
        NullLogger<HttpResilienceService> logger = NullLogger<HttpResilienceService>.Instance;
        NullLogger<DiagnosticsService> diagLogger = NullLogger<DiagnosticsService>.Instance;
        var cfgMonitor = new FileWatchRest.TestUtilities.OptionsMonitorMock<ExternalConfiguration>();
        var diag = new DiagnosticsService(diagLogger, cfgMonitor);

        var handler = new StaticHandler(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var client = new HttpClient(handler);

        var cfg = new ExternalConfiguration { EnableCircuitBreaker = true, CircuitBreakerFailureThreshold = 1, CircuitBreakerOpenDurationMilliseconds = 60000, Retries = 0, RetryDelayMilliseconds = 10 };

        var svc = new HttpResilienceService(logger, diag);

        ResilienceResult first = await svc.SendWithRetriesAsync(ct => Task.FromResult(new HttpRequestMessage(HttpMethod.Get, "http://example/")), client, "ep2", cfg, CancellationToken.None);
        first.Success.Should().BeFalse();
        first.LastStatusCode.Should().Be(500);

        ResilienceResult second = await svc.SendWithRetriesAsync(ct => Task.FromResult(new HttpRequestMessage(HttpMethod.Get, "http://example/")), client, "ep2", cfg, CancellationToken.None);
        second.ShortCircuited.Should().BeTrue();
        second.Attempts.Should().Be(0);
    }

    private sealed class FakeHandler(HttpResponseMessage resp) : HttpMessageHandler {
        private readonly HttpResponseMessage _resp = resp;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(_resp.StatusCode));
    }
}
