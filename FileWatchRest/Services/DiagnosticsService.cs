namespace FileWatchRest.Services;

public class DiagnosticsService : IDisposable
{
    private static readonly Action<ILogger<DiagnosticsService>, string, Exception?> _attemptStartServer =
        LoggerMessage.Define<string>(LogLevel.Information, new EventId(1, "AttemptStartServer"), "Attempting to start diagnostics HTTP server at {Url}");
    private static readonly Action<ILogger<DiagnosticsService>, string, Exception?> _serverStarted =
        LoggerMessage.Define<string>(LogLevel.Information, new EventId(2, "ServerStarted"), "Diagnostics HTTP server started successfully at {Url}");
    private static readonly Action<ILogger<DiagnosticsService>, string, int, Exception?> _failedStartServer =
        LoggerMessage.Define<string, int>(LogLevel.Error, new EventId(3, "FailedStartServer"), "Failed to start diagnostics HTTP server at {Url}. Error code: {ErrorCode}.");
    private static readonly Action<ILogger<DiagnosticsService>, Exception?> _httpServerError =
        LoggerMessage.Define(LogLevel.Warning, new EventId(4, "HttpServerError"), "Error in diagnostics HTTP server");
    private static readonly Action<ILogger<DiagnosticsService>, Exception?> _serializeStatusError =
        LoggerMessage.Define(LogLevel.Error, new EventId(5, "SerializeStatusError"), "Failed to serialize status response");
    private static readonly Action<ILogger<DiagnosticsService>, Exception?> _serializeHealthError =
        LoggerMessage.Define(LogLevel.Error, new EventId(6, "SerializeHealthError"), "Failed to serialize health response");
    private static readonly Action<ILogger<DiagnosticsService>, string, Exception?> _processRequestError =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(7, "ProcessRequestError"), "Error processing diagnostics request for path: {Path}");
    private static readonly Action<ILogger<DiagnosticsService>, Exception?> _disposeError =
        LoggerMessage.Define(LogLevel.Warning, new EventId(8, "DisposeError"), "Error disposing diagnostics service");
    private static readonly Action<ILogger<DiagnosticsService>, string, Exception?> _failedStartServerGeneral =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(9, "FailedStartServerGeneral"), "Failed to start diagnostics HTTP server at {Url}");

    private readonly ConcurrentDictionary<string, int> _restartAttempts = new();
    private readonly ConcurrentDictionary<string, byte> _activeWatchers = new();
    private readonly ConcurrentQueue<FileEventRecord> _events = new();
    private readonly ILogger<DiagnosticsService> _logger;
    private HttpListener? _httpListener;
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _serverTask;
    private readonly Meter _meter = new("FileWatchRest.Metrics", "1.0.0");
    private readonly Counter<long> _processedSuccessCounter;
    private readonly Counter<long> _processedFailureCounter;
    private long _enqueuedTotal;
    private long _processedSuccessTotal;
    private long _processedFailureTotal;
    private readonly ConcurrentBag<IResilienceMetricsProvider> _resilienceProviders = new();
    private string? _requiredBearerToken;
    // Track circuit state per endpoint for diagnostics
    private readonly ConcurrentDictionary<string, CircuitStateInfo> _circuitStates = new();

    public DiagnosticsService(ILogger<DiagnosticsService> logger)
    {
        _logger = logger;
        _processedSuccessCounter = _meter.CreateCounter<long>("file_processed_success_total");
        _processedFailureCounter = _meter.CreateCounter<long>("file_processed_failure_total");
    }

    public void RegisterResilienceMetricsProvider(IResilienceMetricsProvider provider)
    {
        if (provider is null) return;
        _resilienceProviders.Add(provider);
    }

    public void SetBearerToken(string? token)
    {
        _requiredBearerToken = string.IsNullOrWhiteSpace(token) ? null : token;
    }

    public void StartHttpServer(string urlPrefix)
    {
        if (_httpListener != null)
            return; // Already started

        try
        {
            _attemptStartServer(_logger, urlPrefix, null);

            _httpListener = new HttpListener();
            _httpListener.Prefixes.Add(urlPrefix);
            _httpListener.Start();

            _cancellationTokenSource = new CancellationTokenSource();
            _serverTask = Task.Run(() => HandleHttpRequests(_cancellationTokenSource.Token));

            _serverStarted(_logger, urlPrefix, null);
        }
        catch (HttpListenerException ex)
        {
            _failedStartServer(_logger, urlPrefix, ex.ErrorCode, ex);
        }
        catch (Exception ex)
        {
            _failedStartServerGeneral(_logger, urlPrefix, ex);
        }
    }

    private async Task HandleHttpRequests(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _httpListener?.IsListening == true)
        {
            try
            {
                var context = await _httpListener.GetContextAsync();
                _ = Task.Run(() => ProcessRequest(context), cancellationToken);
            }
            catch (ObjectDisposedException)
            {
                break; // HttpListener was disposed
            }
            catch (HttpListenerException)
            {
                break; // HttpListener was stopped
            }
            catch (Exception ex)
            {
                _httpServerError(_logger, ex);
            }
        }
    }

    private void ProcessRequest(HttpListenerContext context)
    {
        try
        {
            var request = context.Request;
            var response = context.Response;

            // Set CORS headers for browser access
            response.Headers.Add("Access-Control-Allow-Origin", "*");
            response.Headers.Add("Access-Control-Allow-Methods", "GET, OPTIONS");
            response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");

            if (request.HttpMethod == "OPTIONS")
            {
                response.StatusCode = 200;
                response.Close();
                return;
            }

            if (request.HttpMethod != "GET")
            {
                response.StatusCode = 405;
                response.Close();
                return;
            }

            var path = request.Url?.AbsolutePath ?? "/";

            // If a bearer token is configured, require it for all endpoints
            if (!string.IsNullOrWhiteSpace(_requiredBearerToken))
            {
                var auth = request.Headers?["Authorization"];
                if (string.IsNullOrWhiteSpace(auth) || !auth.Equals("Bearer " + _requiredBearerToken, StringComparison.Ordinal))
                {
                    response.StatusCode = 401;
                    response.Close();
                    return;
                }
            }

            string responseText;
            string contentType = "application/json";

            switch (path.ToLowerInvariant())
            {
                case "/":
                case "/status":
                    try
                    {
                        var status = new DiagnosticStatus
                        {
                            ActiveWatchers = GetActiveWatchers(),
                            RestartAttempts = GetRestartAttemptsSnapshot(),
                            RecentEvents = GetRecentEvents(200),
                            CircuitStates = GetCircuitStatesSnapshot(),
                            Timestamp = DateTimeOffset.Now,
                            EventCount = _events.Count
                        };
                        responseText = JsonSerializer.Serialize(status, typeof(DiagnosticStatus), MyJsonContext.Default);
                    }
                    catch (Exception ex)
                    {
                        _serializeStatusError(_logger, ex);
                        responseText = $"{{\"error\":\"Serialization failed\",\"message\":\"{ex.Message.Replace("\"", "\\\"")}\",\"timestamp\":\"{DateTimeOffset.Now:O}\"}}";
                    }
                    break;

                case "/health":
                    try
                    {
                        var health = new HealthStatus
                        {
                            Status = "healthy",
                            Timestamp = DateTimeOffset.Now
                        };
                        responseText = JsonSerializer.Serialize(health, typeof(HealthStatus), MyJsonContext.Default);
                    }
                    catch (Exception ex)
                    {
                        _serializeHealthError(_logger, ex);
                        responseText = $"{{\"status\":\"healthy\",\"timestamp\":\"{DateTimeOffset.Now:O}\",\"serializationError\":\"{ex.Message.Replace("\"", "\\\"")}\"}}";
                    }
                    break;

                case "/test":
                    responseText = "{\"message\":\"Hello from FileWatchRest diagnostics\",\"timestamp\":\"" + DateTimeOffset.Now.ToString("O") + "\"}";
                    break;

                case "/events":
                    var events = GetRecentEvents(500).ToArray();
                    responseText = JsonSerializer.Serialize(events, typeof(FileEventRecord[]), MyJsonContext.Default);
                    break;

                case "/watchers":
                    var watchers = GetActiveWatchers().ToArray();
                    responseText = JsonSerializer.Serialize(watchers, typeof(string[]), MyJsonContext.Default);
                    break;

                case "/metrics":
                    // Expose Prometheus plaintext format (a small subset) for simplicity
                    var sb = new StringBuilder();
                    sb.AppendLine("# HELP file_processed_success_total Number of successfully posted files");
                    sb.AppendLine("# TYPE file_processed_success_total counter");
                    sb.AppendLine($"file_processed_success_total {Interlocked.Read(ref _processedSuccessTotal)}");
                    sb.AppendLine("# HELP file_processed_failure_total Number of failed file posts");
                    sb.AppendLine("# TYPE file_processed_failure_total counter");
                    sb.AppendLine($"file_processed_failure_total {Interlocked.Read(ref _processedFailureTotal)}");
                    sb.AppendLine("# HELP file_enqueued_total Number of enqueued files seen at startup");
                    sb.AppendLine("# TYPE file_enqueued_total counter");
                    sb.AppendLine($"file_enqueued_total {Interlocked.Read(ref _enqueuedTotal)}");
                    // Circuit breaker metrics: number open
                    var openCount = _circuitStates.Values.Count(c => c.IsOpen);
                    sb.AppendLine("# HELP circuit_open_total Number of endpoints with open circuit breakers");
                    sb.AppendLine("# TYPE circuit_open_total gauge");
                    sb.AppendLine($"circuit_open_total {openCount}");

                    // Aggregate resilience provider metrics (HTTP attempts/failures/short_circuits)
                    long attemptsSum = 0, failuresSum = 0, shortCircuitsSum = 0;
                    foreach (var p in _resilienceProviders)
                    {
                        try
                        {
                            var s = p.GetMetricsSnapshot();
                            attemptsSum += s.Attempts;
                            failuresSum += s.Failures;
                            shortCircuitsSum += s.ShortCircuits;
                        }
                        catch { }
                    }

                    sb.AppendLine("# HELP http_attempts_total Number of HTTP attempts");
                    sb.AppendLine("# TYPE http_attempts_total counter");
                    sb.AppendLine($"http_attempts_total {attemptsSum}");
                    sb.AppendLine("# HELP http_failures_total Number of HTTP failures");
                    sb.AppendLine("# TYPE http_failures_total counter");
                    sb.AppendLine($"http_failures_total {failuresSum}");
                    sb.AppendLine("# HELP http_short_circuits_total Number of short-circuited HTTP requests");
                    sb.AppendLine("# TYPE http_short_circuits_total counter");
                    sb.AppendLine($"http_short_circuits_total {shortCircuitsSum}");
                    responseText = sb.ToString();
                    contentType = "text/plain";
                    break;

                default:
                    response.StatusCode = 404;
                    var error = new ErrorResponse
                    {
                        Error = "Not found",
                        AvailableEndpoints = new[] { "/status", "/health", "/events", "/watchers", "/test", "/metrics" }
                    };
                    responseText = JsonSerializer.Serialize(error, typeof(ErrorResponse), MyJsonContext.Default);
                    break;
            }

            var buffer = Encoding.UTF8.GetBytes(responseText);
            response.ContentType = contentType + "; charset=utf-8";
            response.ContentLength64 = buffer.Length;
            response.StatusCode = response.StatusCode == 0 ? 200 : response.StatusCode;

            response.OutputStream.Write(buffer, 0, buffer.Length);
            response.Close();
        }
        catch (Exception ex)
        {
            _processRequestError(_logger, context.Request.Url?.AbsolutePath ?? "unknown", ex);
            try
            {
                var errorResponse = "{ \"error\": \"Internal server error\", \"message\": \"" + ex.Message.Replace("\"", "\\\"") + "\" }";
                var errorBuffer = Encoding.UTF8.GetBytes(errorResponse);
                context.Response.StatusCode = 500;
                context.Response.ContentType = "application/json; charset=utf-8";
                context.Response.ContentLength64 = errorBuffer.Length;
                context.Response.OutputStream.Write(errorBuffer, 0, errorBuffer.Length);
                context.Response.Close();
            }
            catch (Exception innerEx)
            {
                _processRequestError(_logger, context.Request.Url?.AbsolutePath ?? "unknown", innerEx);
            }
        }
    }

    public void RegisterWatcher(string folder)
    {
        _activeWatchers[folder] = 1;
    }

    public void UnregisterWatcher(string folder)
    {
        _activeWatchers.TryRemove(folder, out _);
    }

    public int IncrementRestart(string folder)
    {
        return _restartAttempts.AddOrUpdate(folder, 1, (_, cur) => cur + 1);
    }

    public void ResetRestart(string folder)
    {
        _restartAttempts.TryRemove(folder, out _);
    }

    public IReadOnlyDictionary<string, int> GetRestartAttemptsSnapshot() => new Dictionary<string, int>(_restartAttempts);

    public IReadOnlyCollection<string> GetActiveWatchers() => _activeWatchers.Keys.ToList().AsReadOnly();

    public void IncrementEnqueued() => Interlocked.Increment(ref _enqueuedTotal);

    public void RecordFileEvent(string path, bool postedSuccess, int? statusCode)
    {
        _events.Enqueue(new FileEventRecord(path, DateTimeOffset.Now, postedSuccess, statusCode));
        // keep the queue bounded to e.g. 1000 entries
        while (_events.Count > 1000 && _events.TryDequeue(out _)) { }

        // Update counters (also available via System.Diagnostics.Metrics for OTEL)
        if (postedSuccess)
        {
            _processedSuccessCounter.Add(1);
            Interlocked.Increment(ref _processedSuccessTotal);
        }
        else
        {
            _processedFailureCounter.Add(1);
            Interlocked.Increment(ref _processedFailureTotal);
        }
    }

    public void UpdateCircuitState(string endpoint, int failures, DateTimeOffset? openUntil)
    {
        var info = new CircuitStateInfo
        {
            Endpoint = endpoint ?? string.Empty,
            Failures = failures,
            OpenUntil = openUntil
        };
        _circuitStates[endpoint ?? string.Empty] = info;
    }

    public IReadOnlyDictionary<string, CircuitStateInfo> GetCircuitStatesSnapshot()
    {
        return new Dictionary<string, CircuitStateInfo>(_circuitStates);
    }

    public IReadOnlyCollection<FileEventRecord> GetRecentEvents(int limit = 100)
    {
        return _events.Reverse().Take(limit).ToArray();
    }

    public object GetStatus()
    {
        return new {
            ActiveWatchers = GetActiveWatchers(),
            RestartAttempts = GetRestartAttemptsSnapshot(),
            RecentEvents = GetRecentEvents(200),
            Timestamp = DateTimeOffset.Now,
            EventCount = _events.Count
        };
    }

    public void Dispose()
    {
        try
        {
            _cancellationTokenSource?.Cancel();
            _httpListener?.Stop();
            _httpListener?.Close();
            _serverTask?.Wait(TimeSpan.FromSeconds(5));
            _cancellationTokenSource?.Dispose();
        }
        catch (Exception ex)
        {
            _disposeError(_logger, ex);
        }
    }
}
