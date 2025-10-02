namespace FileWatchRest.Services;

internal class HttpResilienceService : IResilienceService
{
    private readonly ILogger<HttpResilienceService> _logger;
    private static readonly Action<ILogger<HttpResilienceService>, string, Exception?> _circuitOpenWarning =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(1, "CircuitOpen"), "Circuit breaker is open for endpoint {Endpoint}");
    private static readonly Action<ILogger<HttpResilienceService>, string, int, int, Exception?> _postingTrace =
        LoggerMessage.Define<string, int, int>(LogLevel.Trace, new EventId(2, "PostingAttempt"), "Posting request to {Endpoint} (attempt {Attempt}/{Attempts})");
    private static readonly Action<ILogger<HttpResilienceService>, int, string, int, int, Exception?> _transientApiWarning =
        LoggerMessage.Define<int, string, int, int>(LogLevel.Warning, new EventId(3, "TransientApiResponse"), "Transient API response {StatusCode} for endpoint {Endpoint} (attempt {Attempt}/{Attempts}) - will retry");
    private static readonly Action<ILogger<HttpResilienceService>, string, int, Exception?> _circuitOpenedError =
        LoggerMessage.Define<string, int>(LogLevel.Error, new EventId(4, "CircuitOpened"), "Circuit breaker opened for endpoint {Endpoint} due to {Failures} consecutive failures");
    private static readonly Action<ILogger<HttpResilienceService>, Exception?> _allAttemptsFailedWarning =
        LoggerMessage.Define(LogLevel.Warning, new EventId(5, "AllAttemptsFailed"), "All attempts failed posting to endpoint");
    private static readonly Action<ILogger<HttpResilienceService>, int, string, Exception?> _circuitOpenedByExceptionError =
        LoggerMessage.Define<int, string>(LogLevel.Error, new EventId(6, "CircuitOpenedByException"), "Circuit breaker opened due to {Failures} consecutive exceptions for endpoint {Endpoint}");
    private static readonly Action<ILogger<HttpResilienceService>, string, int, int, Exception?> _exceptionPostingWillRetry =
        LoggerMessage.Define<string, int, int>(LogLevel.Debug, new EventId(7, "ExceptionPostingWillRetry"), "Exception posting request to {Endpoint} on attempt {Attempt}/{Attempts}; will retry");
    private readonly DiagnosticsService _diagnostics;
    private sealed class CircuitState { public int Failures; public DateTimeOffset? OpenUntil; public readonly object Lock = new(); }
    private readonly ConcurrentDictionary<string, CircuitState> _states = new(StringComparer.OrdinalIgnoreCase);

    public HttpResilienceService(ILogger<HttpResilienceService> logger, DiagnosticsService diagnostics)
    {
        _logger = logger;
        _diagnostics = diagnostics;
    }

    public async Task<ResilienceResult> SendWithRetriesAsync(Func<HttpRequestMessage> requestFactory, HttpClient client, string endpointKey, ExternalConfiguration config, CancellationToken ct)
    {
        var state = _states.GetOrAdd(endpointKey ?? string.Empty, _ => new CircuitState());

        // Check circuit open state
        lock (state.Lock)
        {
            if (state.OpenUntil.HasValue && state.OpenUntil.Value > DateTimeOffset.UtcNow)
            {
                _circuitOpenWarning(_logger, endpointKey ?? string.Empty, null);
                _diagnostics.UpdateCircuitState(endpointKey ?? string.Empty, state.Failures, state.OpenUntil);
                return new ResilienceResult(false, 0, null, null, 0, true);
            }
        }

        int attemptsTotal = Math.Max(0, config.Retries) + 1;
        int attempts = 0;
        Exception? lastException = null;
        HttpResponseMessage? lastResponse = null;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var baseDelayMs = Math.Max(100, config.RetryDelayMilliseconds);

        for (int attempt = 1; attempt <= attemptsTotal; attempt++)
        {
            attempts = attempt;
            try
            {
                using var req = requestFactory();
                if (req is null) throw new InvalidOperationException("requestFactory produced null HttpRequestMessage");

                _postingTrace(_logger, endpointKey ?? string.Empty, attempt, attemptsTotal, null);

                var attemptSw = System.Diagnostics.Stopwatch.StartNew();
                lastResponse = await client.SendAsync(req, ct);
                attemptSw.Stop();

                if (lastResponse.IsSuccessStatusCode)
                {
                    // Success -> reset circuit for this endpoint
                    lock (state.Lock)
                    {
                        state.Failures = 0;
                        state.OpenUntil = null;
                    }
                    _diagnostics.UpdateCircuitState(endpointKey ?? string.Empty, 0, null);
                    _diagnostics.RecordFileEvent("(internal)", true, (int)lastResponse.StatusCode);
                    sw.Stop();
                    var total = sw.ElapsedMilliseconds;
                    return new ResilienceResult(true, attempts, (int)lastResponse.StatusCode, null, total, false);
                }

                // Non-success response handling
                var status = (int)lastResponse.StatusCode;
                if (status >= 500 && attempt < attemptsTotal)
                {
                    _transientApiWarning(_logger, status, endpointKey ?? string.Empty, attempt, attemptsTotal, null);
                    // fall through to delay & retry
                }
                else
                {
                    // Final non-success -> mark failure and potentially open circuit
                    if (config.EnableCircuitBreaker)
                    {
                        lock (state.Lock)
                        {
                            state.Failures++;
                            if (state.Failures >= config.CircuitBreakerFailureThreshold)
                            {
                                state.OpenUntil = DateTimeOffset.UtcNow.AddMilliseconds(config.CircuitBreakerOpenDurationMilliseconds);
                                _circuitOpenedError(_logger, endpointKey ?? string.Empty, state.Failures, null);
                            }
                        }
                        _diagnostics.UpdateCircuitState(endpointKey ?? string.Empty, state.Failures, state.OpenUntil);
                    }
                    _diagnostics.RecordFileEvent("(internal)", false, status);
                    sw.Stop();
                    return new ResilienceResult(false, attempts, status, null, sw.ElapsedMilliseconds, false);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                sw.Stop();
                return new ResilienceResult(false, attempts, null, null, sw.ElapsedMilliseconds, false);
            }
            catch (Exception ex)
            {
                lastException = ex;
                _diagnostics.RecordFileEvent("(internal)", false, null);
                if (attempt < attemptsTotal)
                {
                    _exceptionPostingWillRetry(_logger, endpointKey ?? string.Empty, attempt, attemptsTotal, ex);
                }
                else
                {
                    _allAttemptsFailedWarning(_logger, ex);
                }

                if (config.EnableCircuitBreaker && attempt >= attemptsTotal)
                {
                    lock (state.Lock)
                    {
                        state.Failures++;
                        if (state.Failures >= config.CircuitBreakerFailureThreshold)
                        {
                            state.OpenUntil = DateTimeOffset.UtcNow.AddMilliseconds(config.CircuitBreakerOpenDurationMilliseconds);
                            _circuitOpenedByExceptionError(_logger, state.Failures, endpointKey ?? string.Empty, null);
                        }
                    }
                    _diagnostics.UpdateCircuitState(endpointKey ?? string.Empty, state.Failures, state.OpenUntil);
                }
            }

            // Delay before next attempt (if any)
            if (attempt < attemptsTotal)
            {
                var jitter = Random.Shared.Next(0, 100);
                var delay = (baseDelayMs << (attempt - 1)) + jitter;
                try { await Task.Delay(delay, ct); } catch (OperationCanceledException) when (ct.IsCancellationRequested) { sw.Stop(); return new ResilienceResult(false, attempts, null, lastException, sw.ElapsedMilliseconds, false); }
            }
        }

        sw.Stop();
        return new ResilienceResult(false, attempts, null, lastException, sw.ElapsedMilliseconds, false);
    }
}
