using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using FileWatchRest.Services;
using FileWatchRest.Configuration;

namespace FileWatchRest.Tests.Services
{
    public class HttpResilienceServiceTests
    {
        private sealed class StaticHandler : HttpMessageHandler
        {
            private readonly HttpResponseMessage _response;
            public StaticHandler(HttpResponseMessage response) => _response = response;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                return Task.FromResult(new HttpResponseMessage(_response.StatusCode) { Content = _response.Content });
            }
        }

        [Fact]
        public async Task SendWithRetriesAsync_success_returns_success()
        {
            var logger = NullLogger<HttpResilienceService>.Instance;
            var diagLogger = NullLogger<DiagnosticsService>.Instance;
            var cfgMonitor = new FileWatchRest.TestUtilities.OptionsMonitorMock<ExternalConfiguration>();
            var diag = new DiagnosticsService(diagLogger, cfgMonitor);

            var handler = new StaticHandler(new HttpResponseMessage(HttpStatusCode.OK));
            var client = new HttpClient(handler);

            var svc = new HttpResilienceService(logger, diag);

            var result = await svc.SendWithRetriesAsync(ct => Task.FromResult(new HttpRequestMessage(HttpMethod.Get, "http://example/")), client, "ep", new ExternalConfiguration { Retries = 0, RetryDelayMilliseconds = 100 }, CancellationToken.None);

            result.Success.Should().BeTrue();
            result.Attempts.Should().Be(1);
            result.LastStatusCode.Should().Be(200);
            result.ShortCircuited.Should().BeFalse();
        }

        [Fact]
        public async Task SendWithRetriesAsync_failure_opens_circuit_and_short_circuits_next_call()
        {
            var logger = NullLogger<HttpResilienceService>.Instance;
            var diagLogger = NullLogger<DiagnosticsService>.Instance;
            var cfgMonitor = new FileWatchRest.TestUtilities.OptionsMonitorMock<ExternalConfiguration>();
            var diag = new DiagnosticsService(diagLogger, cfgMonitor);

            var handler = new StaticHandler(new HttpResponseMessage(HttpStatusCode.InternalServerError));
            var client = new HttpClient(handler);

            var cfg = new ExternalConfiguration { EnableCircuitBreaker = true, CircuitBreakerFailureThreshold = 1, CircuitBreakerOpenDurationMilliseconds = 60000, Retries = 0, RetryDelayMilliseconds = 10 };

            var svc = new HttpResilienceService(logger, diag);

            var first = await svc.SendWithRetriesAsync(ct => Task.FromResult(new HttpRequestMessage(HttpMethod.Get, "http://example/")), client, "ep2", cfg, CancellationToken.None);
            first.Success.Should().BeFalse();
            first.LastStatusCode.Should().Be(500);

            var second = await svc.SendWithRetriesAsync(ct => Task.FromResult(new HttpRequestMessage(HttpMethod.Get, "http://example/")), client, "ep2", cfg, CancellationToken.None);
            second.ShortCircuited.Should().BeTrue();
            second.Attempts.Should().Be(0);
        }

        private sealed class FakeHandler : HttpMessageHandler
        {
            private readonly HttpResponseMessage _resp;
            public FakeHandler(HttpResponseMessage resp) => _resp = resp;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(_resp.StatusCode));
        }
    }
}
