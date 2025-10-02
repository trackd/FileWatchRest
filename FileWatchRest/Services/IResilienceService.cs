namespace FileWatchRest.Services;

public interface IResilienceService
{
    /// <summary>
    /// Execute an HTTP send operation using a fresh HttpRequestMessage produced by requestFactory for each attempt.
    /// Returns a small result summarizing success, attempts, last status code and last exception.
    /// The service maintains a per-endpoint circuit breaker and will short-circuit if the endpoint circuit is open.
    /// </summary>
    Task<ResilienceResult> SendWithRetriesAsync(Func<HttpRequestMessage> requestFactory, HttpClient client, string endpointKey, ExternalConfiguration config, CancellationToken ct);
}

public sealed record ResilienceResult(bool Success, int Attempts, int? LastStatusCode, Exception? LastException, long TotalElapsedMs, bool ShortCircuited);
