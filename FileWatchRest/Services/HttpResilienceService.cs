namespace FileWatchRest.Services;

internal class HttpResilienceService : IResilienceService, IResilienceMetricsProvider, IDisposable {
    private readonly ILogger<HttpResilienceService> _logger;
    /// <summary>
    /// Logger delegates moved to LoggerDelegates.cs for unified management
    /// </summary>
    private readonly DiagnosticsService _diagnostics;
    private readonly Meter _meter = new("FileWatchRest.HttpResilience", "1.0.0");
    private readonly Counter<long> _attemptsCounter;
    private readonly Counter<long> _failureCounter;
    private readonly Counter<long> _shortCircuitCounter;
    /// <summary>
    /// Totals for diagnostics snapshot (counters don't expose totals)
    /// </summary>
    private long _attemptsTotal;
    private long _failuresTotal;
    private long _shortCircuitTotal;
    private sealed class CircuitState { public int Failures; public DateTimeOffset? OpenUntil; public readonly object Lock = new(); }
    private readonly ConcurrentDictionary<string, CircuitState> _states = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>
    /// Prevent unbounded memory growth
    /// </summary>
    private const int MaxCircuitStates = 100;

    public HttpResilienceService(ILogger<HttpResilienceService> logger, DiagnosticsService diagnostics) {
        _logger = logger;
        _diagnostics = diagnostics;
        _attemptsCounter = _meter.CreateCounter<long>("http_attempts_total");
        _failureCounter = _meter.CreateCounter<long>("http_failures_total");
        _shortCircuitCounter = _meter.CreateCounter<long>("http_short_circuits_total");

        // Register with diagnostics so totals can be exposed
        _diagnostics.RegisterResilienceMetricsProvider(this);
    }

    public ResilienceMetrics GetMetricsSnapshot() {
        return new ResilienceMetrics(
            Interlocked.Read(ref _attemptsTotal),
            Interlocked.Read(ref _failuresTotal),
            Interlocked.Read(ref _shortCircuitTotal));
    }

    public async Task<ResilienceResult> SendWithRetriesAsync(Func<CancellationToken, Task<HttpRequestMessage>> requestFactory, HttpClient client, string endpointKey, ExternalConfiguration config, CancellationToken ct) {
        // Prune old circuit states if we've exceeded the maximum
        if (_states.Count > MaxCircuitStates) {
            // Remove the oldest opened or least recently failed circuit
            string? oldestKey = _states
                .OrderBy(kv => kv.Value.OpenUntil ?? DateTimeOffset.MinValue)
                .FirstOrDefault().Key;
            if (oldestKey is not null) {
                _states.TryRemove(oldestKey, out _);
            }
        }

        CircuitState state = _states.GetOrAdd(endpointKey ?? string.Empty, _ => new CircuitState());

        // Check circuit open state
        lock (state.Lock) {
            if (state.OpenUntil.HasValue && state.OpenUntil.Value > DateTimeOffset.UtcNow) {
                LoggerDelegates.CircuitOpenWarning(_logger, endpointKey ?? string.Empty, null);
                _diagnostics.UpdateCircuitState(endpointKey ?? string.Empty, state.Failures, state.OpenUntil);
                _shortCircuitCounter.Add(1);
                Interlocked.Increment(ref _shortCircuitTotal);
                return new ResilienceResult(false, 0, null, null, 0, true);
            }
        }

        int attemptsTotal = Math.Max(0, config.Retries) + 1;
        int attempts = 0;
        Exception? lastException = null;
        HttpResponseMessage? lastResponse = null;
        var sw = Stopwatch.StartNew();

        int baseDelayMs = Math.Max(100, config.RetryDelayMilliseconds);

        for (int attempt = 1; attempt <= attemptsTotal; attempt++) {
            attempts = attempt;
            try {
                _attemptsCounter.Add(1);
                Interlocked.Increment(ref _attemptsTotal);
                using HttpRequestMessage req = await requestFactory(ct).ConfigureAwait(false) ?? throw new InvalidOperationException("requestFactory produced null HttpRequestMessage");
                LoggerDelegates.PostingTrace(_logger, endpointKey ?? string.Empty, attempt, attemptsTotal, null);

                var attemptSw = Stopwatch.StartNew();
                lastResponse = await client.SendAsync(req, ct).ConfigureAwait(false);
                attemptSw.Stop();

                if (lastResponse.IsSuccessStatusCode) {
                    // Success -> reset circuit for this endpoint
                    lock (state.Lock) {
                        state.Failures = 0;
                        state.OpenUntil = null;
                    }
                    _diagnostics.UpdateCircuitState(endpointKey ?? string.Empty, 0, null);
                    _diagnostics.RecordFileEvent("(internal)", true, (int)lastResponse.StatusCode);
                    sw.Stop();
                    long total = sw.ElapsedMilliseconds;
                    return new ResilienceResult(true, attempts, (int)lastResponse.StatusCode, null, total, false);
                }

                // Non-success response handling
                int status = (int)lastResponse.StatusCode;
                if (status >= 500 && attempt < attemptsTotal) {
                    LoggerDelegates.TransientApiWarning(_logger, status, endpointKey ?? string.Empty, attempt, attemptsTotal, null);
                    // fall through to delay & retry
                }
                else {
                    // Final non-success -> mark failure and potentially open circuit
                    if (config.EnableCircuitBreaker) {
                        lock (state.Lock) {
                            state.Failures++;
                            if (state.Failures >= config.CircuitBreakerFailureThreshold) {
                                state.OpenUntil = DateTimeOffset.UtcNow.AddMilliseconds(config.CircuitBreakerOpenDurationMilliseconds);
                                LoggerDelegates.CircuitOpenedError(_logger, endpointKey ?? string.Empty, state.Failures, null);
                            }
                        }
                        _diagnostics.UpdateCircuitState(endpointKey ?? string.Empty, state.Failures, state.OpenUntil);
                    }
                    _diagnostics.RecordFileEvent("(internal)", false, status);
                    _failureCounter.Add(1);
                    Interlocked.Increment(ref _failuresTotal);
                    sw.Stop();
                    return new ResilienceResult(false, attempts, status, null, sw.ElapsedMilliseconds, false);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) {
                sw.Stop();
                return new ResilienceResult(false, attempts, null, null, sw.ElapsedMilliseconds, false);
            }
            catch (Exception ex) {
                lastException = ex;
                _diagnostics.RecordFileEvent("(internal)", false, null);
                if (attempt < attemptsTotal) {
                    LoggerDelegates.ExceptionPostingWillRetry(_logger, endpointKey ?? string.Empty, attempt, attemptsTotal, ex);
                }
                else {
                    LoggerDelegates.AllAttemptsFailedWarning(_logger, ex);
                }

                if (config.EnableCircuitBreaker && attempt >= attemptsTotal) {
                    lock (state.Lock) {
                        state.Failures++;
                        if (state.Failures >= config.CircuitBreakerFailureThreshold) {
                            state.OpenUntil = DateTimeOffset.UtcNow.AddMilliseconds(config.CircuitBreakerOpenDurationMilliseconds);
                            LoggerDelegates.CircuitOpenedByExceptionError(_logger, state.Failures, endpointKey ?? string.Empty, null);
                        }
                    }
                    _diagnostics.UpdateCircuitState(endpointKey ?? string.Empty, state.Failures, state.OpenUntil);
                }
            }

            // Delay before next attempt (if any)
            if (attempt < attemptsTotal) {
                int jitter = Random.Shared.Next(0, 100);
                int delay = (baseDelayMs << (attempt - 1)) + jitter;
                try { await Task.Delay(delay, ct); } catch (OperationCanceledException) when (ct.IsCancellationRequested) { sw.Stop(); return new ResilienceResult(false, attempts, null, lastException, sw.ElapsedMilliseconds, false); }
            }
        }

        sw.Stop();
        return new ResilienceResult(false, attempts, null, lastException, sw.ElapsedMilliseconds, false);
    }

    public void Dispose() =>
        // No unmanaged resources; ensure any registered metrics/providers are cleaned up if needed.
        // Unregistering from diagnostics isn't required here (DiagnosticsService keeps a ConcurrentBag),
        // so perform a safe no-op and suppress finalization to satisfy analyzer.
        GC.SuppressFinalize(this);
}
