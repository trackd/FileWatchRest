namespace FileWatchRest.Tests;

public class HttpResilienceServiceCircuitTests
{
    [Fact]
    public async Task CircuitBreaker_Opens_After_Threshold_And_ShortCircuits()
    {
        var factory = LoggerFactory.Create(b => b.AddDebug());
        var diagLogger = factory.CreateLogger<DiagnosticsService>();
        var resilienceLogger = factory.CreateLogger<HttpResilienceService>();
        var diagnostics = new DiagnosticsService(diagLogger);

        // Handler that always returns 500 and records calls
        int callCount = 0;
        var handler = new DelegatingHandlerStub((req, ct) =>
        {
            Interlocked.Increment(ref callCount);
            var res = new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("err") };
            return Task.FromResult(res);
        });

        using var client = new HttpClient(handler);
        var svc = new HttpResilienceService(resilienceLogger, diagnostics);

        var cfg = new ExternalConfiguration
        {
            EnableCircuitBreaker = true,
            CircuitBreakerFailureThreshold = 2,
            CircuitBreakerOpenDurationMilliseconds = 60_000,
            Retries = 0,
            RetryDelayMilliseconds = 1
        };

    Func<CancellationToken, Task<HttpRequestMessage>> reqFactory = ct => Task.FromResult(new HttpRequestMessage(HttpMethod.Get, "http://localhost/test"));

        // First two calls should attempt HTTP and fail
        var r1 = await svc.SendWithRetriesAsync(reqFactory, client, "ep", cfg, CancellationToken.None);
        r1.Success.Should().BeFalse();
        var r2 = await svc.SendWithRetriesAsync(reqFactory, client, "ep", cfg, CancellationToken.None);
        r2.Success.Should().BeFalse();

        // At this point the circuit should have opened; subsequent call should be short-circuited without new HTTP calls
        var r3 = await svc.SendWithRetriesAsync(reqFactory, client, "ep", cfg, CancellationToken.None);
        r3.ShortCircuited.Should().BeTrue();

        // Ensure handler was called exactly twice (first two attempts)
        callCount.Should().Be(2);
    }

    private class DelegatingHandlerStub : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _fn;
        public DelegatingHandlerStub(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> fn) => _fn = fn;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => _fn(request, cancellationToken);
    }
}
