namespace FileWatchRest.Tests;

public class HttpResilienceServiceCircuitTests {
    [Fact]
    public async Task CircuitBreakerOpensAfterThresholdAndShortCircuits() {
        ILoggerFactory factory = LoggerFactory.Create(b => b.AddDebug());
        ILogger<DiagnosticsService> diagLogger = factory.CreateLogger<DiagnosticsService>();
        ILogger<HttpResilienceService> resilienceLogger = factory.CreateLogger<HttpResilienceService>();
        var diagnostics = new DiagnosticsService(diagLogger, new OptionsMonitorMock<ExternalConfiguration>());

        // Handler that always returns 500 and records calls
        int callCount = 0;
        var handler = new DelegatingHandlerStub((req, ct) => {
            Interlocked.Increment(ref callCount);
            var res = new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("err") };
            return Task.FromResult(res);
        });

        using var client = new HttpClient(handler);
        var svc = new HttpResilienceService(resilienceLogger, diagnostics);

        var cfg = new ExternalConfiguration {
            EnableCircuitBreaker = true,
            CircuitBreakerFailureThreshold = 2,
            CircuitBreakerOpenDurationMilliseconds = 60_000,
            Retries = 0,
            RetryDelayMilliseconds = 1
        };

        static Task<HttpRequestMessage> reqFactory(CancellationToken ct) {
            return Task.FromResult(new HttpRequestMessage(HttpMethod.Get, "http://localhost/test"));
        }

        // First two calls should attempt HTTP and fail
        ResilienceResult r1 = await svc.SendWithRetriesAsync(reqFactory, client, "ep", cfg, CancellationToken.None);
        Assert.False(r1.Success);
        ResilienceResult r2 = await svc.SendWithRetriesAsync(reqFactory, client, "ep", cfg, CancellationToken.None);
        Assert.False(r2.Success);

        // At this point the circuit should have opened; subsequent call should be short-circuited without new HTTP calls
        ResilienceResult r3 = await svc.SendWithRetriesAsync(reqFactory, client, "ep", cfg, CancellationToken.None);
        Assert.True(r3.ShortCircuited);

        // Ensure handler was called exactly twice (first two attempts)
        Assert.Equal(2, callCount);
    }

    private sealed class DelegatingHandlerStub(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> fn) : HttpMessageHandler {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _fn = fn;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => _fn(request, cancellationToken);
    }
}
