namespace FileWatchRest.Services;

/// <summary>
/// Provides diagnostics and metrics for file watcher and notification operations.
/// </summary>
public class DiagnosticsService : IDiagnosticsService {
    /// <summary>
    /// Returns true if the file at the given path has been posted and acknowledged (HTTP 200).
    /// </summary>
    /// <param name="path"></param>
    public bool IsFilePosted(string path) => !string.IsNullOrWhiteSpace(path) && _postedFileStatus.TryGetValue(path, out bool status) && status;
    private readonly ConcurrentDictionary<string, bool> _postedFileStatus = new(StringComparer.OrdinalIgnoreCase);
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
    private readonly ConcurrentBag<IResilienceMetricsProvider> _resilienceProviders = [];
    private string? _requiredBearerToken;
    private string? _currentPrefix;
    /// <summary>
    /// Track circuit state per endpoint for diagnostics
    /// </summary>
    private readonly ConcurrentDictionary<string, CircuitStateInfo> _circuitStates = new();
    /// <summary>
    /// Reference to the current configuration for diagnostics
    /// </summary>
    private readonly IOptionsMonitor<ExternalConfiguration> _configMonitor;
    /// <summary>
    /// Optional live runtime configuration set by the Worker after normalization
    /// </summary>
    private ExternalConfiguration? _liveConfig;

    /// <summary>
    /// Initializes a new instance of the <see cref="DiagnosticsService"/> class.
    /// </summary>
    /// <param name="logger">Logger for diagnostics output.</param>
    /// <param name="configMonitor">Options monitor for configuration changes.</param>
    public DiagnosticsService(ILogger<DiagnosticsService> logger, IOptionsMonitor<ExternalConfiguration> configMonitor) {
        _logger = logger;
        _configMonitor = configMonitor;
        // Initialize _currentPrefix from initial config value if present; if none provided, default to localhost
        string? initialPrefix = _configMonitor.CurrentValue?.DiagnosticsUrlPrefix;
        _currentPrefix = string.IsNullOrWhiteSpace(initialPrefix) ? "http://localhost:5005/" : initialPrefix;
        _processedSuccessCounter = _meter.CreateCounter<long>("file_processed_success_total");
        _processedFailureCounter = _meter.CreateCounter<long>("file_processed_failure_total");
        // Do not auto-start the HTTP server in the constructor to avoid prefix conflicts
        // Tests should call `StartHttpServer` with an explicit prefix when needed.
    }

    public void RegisterResilienceMetricsProvider(IResilienceMetricsProvider provider) {
        if (provider is null) {
            return;
        }

        _resilienceProviders.Add(provider);
    }

    public void SetBearerToken(string? token) => _requiredBearerToken = string.IsNullOrWhiteSpace(token) ? null : token;

    /// <summary>
    /// Set the live runtime configuration. This should be called by the Worker after
    /// loading and normalizing configuration so diagnostics returns the exact running config.
    /// </summary>
    /// <param name="cfg">Normalized external configuration instance.</param>
    public void SetConfiguration(ExternalConfiguration? cfg) => _liveConfig = cfg;

    /// <summary>
    /// Expose the current HTTP prefix used by the diagnostics server
    /// </summary>
    public string CurrentPrefix => _currentPrefix ?? string.Empty;

    /// <summary>
    /// Restart the diagnostics HTTP server with a new prefix
    /// </summary>
    /// <param name="urlPrefix"></param>
    public void RestartHttpServer(string urlPrefix) {
        try {
            if (_httpListener is not null) {
                try {
                    _cancellationTokenSource?.Cancel();
                    _httpListener.Stop();
                    _httpListener.Close();
                }
                catch { }
                finally {
                    _httpListener = null;
                    _serverTask = null;
                    _cancellationTokenSource?.Dispose();
                    _cancellationTokenSource = null;
                }
            }

            if (!string.IsNullOrWhiteSpace(urlPrefix)) {
                StartHttpServer(urlPrefix);
            }
        }
        catch (Exception ex) {
            LoggerDelegates.FailedStartServerGeneral(_logger, urlPrefix, ex);
        }
    }

    public void StartHttpServer(string urlPrefix) {
        if (_httpListener is not null) {
            // If already started with the same prefix, nothing to do
            if (string.Equals(_currentPrefix, urlPrefix, StringComparison.OrdinalIgnoreCase)) {
                return;
            }

            // If a different prefix is requested, restart to bind the new prefix
            RestartHttpServer(urlPrefix);
            return;
        }

        try {
            LoggerDelegates.AttemptStartServer(_logger, urlPrefix, null);

            _httpListener = new HttpListener();
            _httpListener.Prefixes.Add(urlPrefix);
            _httpListener.Start();

            _cancellationTokenSource = new CancellationTokenSource();
            _serverTask = Task.Run(() => HandleHttpRequests(_cancellationTokenSource.Token));

            _currentPrefix = urlPrefix;
            LoggerDelegates.ServerStarted(_logger, urlPrefix, null);
        }
        catch (HttpListenerException ex) {
            LoggerDelegates.FailedStartServer(_logger, urlPrefix, ex.ErrorCode, ex);
        }
        catch (Exception ex) {
            LoggerDelegates.FailedStartServerGeneral(_logger, urlPrefix, ex);
        }
    }

    private async Task HandleHttpRequests(CancellationToken cancellationToken) {
        while (!cancellationToken.IsCancellationRequested && _httpListener?.IsListening == true) {
            try {
                HttpListenerContext context = await _httpListener.GetContextAsync();
                _ = Task.Run(() => ProcessRequest(context), cancellationToken);
            }
            catch (ObjectDisposedException) {
                break; // HttpListener was disposed
            }
            catch (HttpListenerException) {
                break; // HttpListener was stopped
            }
            catch (Exception ex) {
                LoggerDelegates.HttpServerError(_logger, ex);
            }
        }
    }

    private void ProcessRequest(HttpListenerContext context) {
        try {
            HttpListenerRequest request = context.Request;
            HttpListenerResponse response = context.Response;

            // Set CORS headers for browser access
            response.Headers.Add("Access-Control-Allow-Origin", "*");
            response.Headers.Add("Access-Control-Allow-Methods", "GET, OPTIONS");
            response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");

            if (request.HttpMethod == "OPTIONS") {
                response.StatusCode = 200;
                response.Close();
                return;
            }

            if (request.HttpMethod != "GET") {
                response.StatusCode = 405;
                response.Close();
                return;
            }

            string path = request.Url?.AbsolutePath ?? "/";

            // Allow unauthenticated access to the /config endpoint for diagnostics (tests and safe read-only scenarios)
            string pathLower = path.ToLowerInvariant();
            if (!pathLower.Equals("/config", StringComparison.OrdinalIgnoreCase)) {
                // If a bearer token is configured (not null/empty), require it for all other endpoints
                if (_requiredBearerToken is not null) {
                    string? auth = request.Headers?["Authorization"];
                    if (string.IsNullOrWhiteSpace(_requiredBearerToken)) {
                        // If token is empty string, treat as no auth required
                    }
                    else if (string.IsNullOrWhiteSpace(auth) || !auth.Equals("Bearer " + _requiredBearerToken, StringComparison.Ordinal)) {
                        response.StatusCode = 401;
                        response.Close();
                        return;
                    }
                }
            }

            string responseText;
            string contentType = "application/json";

            switch (path.ToLowerInvariant()) {
                case "/":
                case "/status":
                    try {
                        var status = new DiagnosticStatus {
                            ActiveWatchers = GetActiveWatchers(),
                            RestartAttempts = GetRestartAttemptsSnapshot(),
                            RecentEvents = GetRecentEvents(200),
                            CircuitStates = GetCircuitStatesSnapshot(),
                            Timestamp = DateTimeOffset.Now,
                            EventCount = _events.Count
                        };
                        responseText = JsonSerializer.Serialize(status, typeof(DiagnosticStatus), MyJsonContext.Default);
                    }
                    catch (Exception ex) {
                        LoggerDelegates.SerializeStatusError(_logger, ex);
                        responseText = $"{{\"error\":\"Serialization failed\",\"message\":\"{ex.Message.Replace("\"", "\\\"")}\",\"timestamp\":\"{DateTimeOffset.Now:O}\"}}";
                    }

                    break;

                case "/config":
                    // Prefer live configuration set by the worker (normalized), fall back to options monitor
                    ExternalConfiguration? config = _liveConfig ?? _configMonitor.CurrentValue;
                    if (config is not null) {
                        // Export as an ExternalConfiguration-shaped object so callers that
                        // deserialize into ExternalConfiguration (tests) receive the
                        // normalized typed `Folders` list and `Actions` used at runtime.
                        var export = new ExternalConfiguration {
                            ApiEndpoint = config.ApiEndpoint,
                            BearerToken = config.BearerToken,
                            PostFileContents = config.PostFileContents,
                            ProcessedFolder = config.ProcessedFolder,
                            MoveProcessedFiles = config.MoveProcessedFiles,
                            AllowedExtensions = config.AllowedExtensions ?? [],
                            IncludeSubdirectories = config.IncludeSubdirectories,
                            DebounceMilliseconds = config.DebounceMilliseconds,
                            Retries = config.Retries,
                            RetryDelayMilliseconds = config.RetryDelayMilliseconds,
                            DiagnosticsUrlPrefix = config.DiagnosticsUrlPrefix,
                            ChannelCapacity = config.ChannelCapacity,
                            MaxParallelSends = config.MaxParallelSends,
                            FileWatcherInternalBufferSize = config.FileWatcherInternalBufferSize,
                            Logging = config.Logging,
                            // Export typed folders list so callers receive the normalized runtime shape
                            Folders = config.Folders ?? [],
                            // Export per-action configurations as well
                            Actions = config.Actions ?? []
                        };

                        responseText = JsonSerializer.Serialize(export, typeof(ExternalConfiguration), MyJsonContext.Default);
                    }
                    else {
                        responseText = /*lang=json,strict*/ "{\"error\":\"No configuration loaded\"}";
                    }
                    break;

                case "/health":
                    try {
                        var health = new HealthStatus {
                            Status = "healthy",
                            Timestamp = DateTimeOffset.Now
                        };
                        responseText = JsonSerializer.Serialize(health, typeof(HealthStatus), MyJsonContext.Default);
                    }
                    catch (Exception ex) {
                        LoggerDelegates.SerializeHealthError(_logger, ex);
                        responseText = $"{{\"status\":\"healthy\",\"timestamp\":\"{DateTimeOffset.Now:O}\",\"serializationError\":\"{ex.Message.Replace("\"", "\\\"")}\"}}";
                    }
                    break;

                case "/test":
                    responseText = "{\"message\":\"Hello from FileWatchRest diagnostics\",\"timestamp\":\"" + DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture) + "\"}";
                    break;

                case "/events":
                    FileEventRecord[] events = [.. GetRecentEvents(500)];
                    responseText = JsonSerializer.Serialize(events, typeof(FileEventRecord[]), MyJsonContext.Default);
                    break;

                case "/watchers":
                    string[] watchers = [.. GetActiveWatchers()];
                    responseText = JsonSerializer.Serialize(watchers, typeof(string[]), MyJsonContext.Default);
                    break;

                case "/metrics":
                    // Expose Prometheus plaintext format (a small subset) for simplicity
                    var sb = new StringBuilder();
                    sb.AppendLine("# HELP file_processed_success_total Number of successfully posted files");
                    sb.AppendLine("# TYPE file_processed_success_total counter");
                    sb.AppendLine("file_processed_success_total " + Interlocked.Read(ref _processedSuccessTotal).ToString(CultureInfo.InvariantCulture));
                    sb.AppendLine("# HELP file_processed_failure_total Number of failed file posts");
                    sb.AppendLine("# TYPE file_processed_failure_total counter");
                    sb.AppendLine("file_processed_failure_total " + Interlocked.Read(ref _processedFailureTotal).ToString(CultureInfo.InvariantCulture));
                    sb.AppendLine("# HELP file_enqueued_total Number of enqueued files seen at startup");
                    sb.AppendLine("# TYPE file_enqueued_total counter");
                    sb.AppendLine("file_enqueued_total " + Interlocked.Read(ref _enqueuedTotal).ToString(CultureInfo.InvariantCulture));
                    // Circuit breaker metrics: number open
                    int openCount = _circuitStates.Values.Count(c => c.IsOpen);
                    sb.AppendLine("# HELP circuit_open_total Number of endpoints with open circuit breakers");
                    sb.AppendLine("# TYPE circuit_open_total gauge");
                    sb.AppendLine("circuit_open_total " + openCount.ToString(CultureInfo.InvariantCulture));

                    // Aggregate resilience provider metrics (HTTP attempts/failures/short_circuits)
                    long attemptsSum = 0, failuresSum = 0, shortCircuitsSum = 0;
                    foreach (IResilienceMetricsProvider p in _resilienceProviders) {
                        try {
                            ResilienceMetrics s = p.GetMetricsSnapshot();
                            attemptsSum += s.Attempts;
                            failuresSum += s.Failures;
                            shortCircuitsSum += s.ShortCircuits;
                        }
                        catch { }
                    }

                    sb.AppendLine("# HELP http_attempts_total Number of HTTP attempts");
                    sb.AppendLine("# TYPE http_attempts_total counter");
                    sb.AppendLine("http_attempts_total " + attemptsSum.ToString(CultureInfo.InvariantCulture));
                    sb.AppendLine("# HELP http_failures_total Number of HTTP failures");
                    sb.AppendLine("# TYPE http_failures_total counter");
                    sb.AppendLine("http_failures_total " + failuresSum.ToString(CultureInfo.InvariantCulture));
                    sb.AppendLine("# HELP http_short_circuits_total Number of short-circuited HTTP requests");
                    sb.AppendLine("# TYPE http_short_circuits_total counter");
                    sb.AppendLine("http_short_circuits_total " + shortCircuitsSum.ToString(CultureInfo.InvariantCulture));
                    responseText = sb.ToString();
                    contentType = "text/plain";
                    break;

                default:
                    response.StatusCode = 404;
                    var error = new ErrorResponse {
                        Error = "Not found",
                        AvailableEndpoints = ["/status", "/health", "/events", "/watchers", "/test", "/metrics", "/config"]
                    };
                    responseText = JsonSerializer.Serialize(error, typeof(ErrorResponse), MyJsonContext.Default);
                    break;
            }

            byte[] buffer = Encoding.UTF8.GetBytes(responseText);
            response.ContentType = contentType + "; charset=utf-8";
            response.ContentLength64 = buffer.Length;
            response.StatusCode = response.StatusCode == 0 ? 200 : response.StatusCode;

            response.OutputStream.Write(buffer, 0, buffer.Length);
            response.Close();
        }
        catch (Exception ex) {
            LoggerDelegates.ProcessRequestError(_logger, context.Request.Url?.AbsolutePath ?? "unknown", ex);
            try {
                string errorResponse = "{ \"error\": \"Internal server error\", \"message\": \"" + ex.Message.Replace("\"", "\\\"") + "\" }";
                byte[] errorBuffer = Encoding.UTF8.GetBytes(errorResponse);
                context.Response.StatusCode = 500;
                context.Response.ContentType = "application/json; charset=utf-8";
                context.Response.ContentLength64 = errorBuffer.Length;
                context.Response.OutputStream.Write(errorBuffer, 0, errorBuffer.Length);
                context.Response.Close();
            }
            catch (Exception innerEx) {
                LoggerDelegates.ProcessRequestError(_logger, context.Request.Url?.AbsolutePath ?? "unknown", innerEx);
            }
        }
    }

    /// <summary>
    /// Records a file event for diagnostics and metrics.
    /// </summary>
    /// <param name="path">File path.</param>
    /// <param name="success">True if notification was posted successfully.</param>
    /// <param name="statusCode">HTTP status code, if available.</param>
    public void RecordFileEvent(string path, bool success, int? statusCode) {
        _events.Enqueue(new FileEventRecord(path, DateTimeOffset.Now, success, statusCode));
        Interlocked.Increment(ref _enqueuedTotal);
        // keep the queue bounded to e.g. 1000 entries
        while (_events.Count > 1000 && _events.TryDequeue(out _)) { }

        // Update per-file post status cache (only mark as posted if HTTP 200)
        if (!string.IsNullOrWhiteSpace(path)) {
            if (success && statusCode == 200) {
                _postedFileStatus[path] = true;
            }
            else if (!success) {
                _postedFileStatus[path] = false;
            }
        }

        // Update counters (also available via System.Diagnostics.Metrics for OTEL)
        if (success) {
            _processedSuccessCounter.Add(1);
            Interlocked.Increment(ref _processedSuccessTotal);
        }
        else {
            _processedFailureCounter.Add(1);
            Interlocked.Increment(ref _processedFailureTotal);
        }
    }

    public void UpdateCircuitState(string endpoint, int failures, DateTimeOffset? openUntil) {
        _circuitStates[endpoint ?? string.Empty] = new CircuitStateInfo {
            Endpoint = endpoint ?? string.Empty,
            Failures = failures,
            OpenUntil = openUntil
        };
    }

    public IReadOnlyDictionary<string, CircuitStateInfo> GetCircuitStatesSnapshot() => new Dictionary<string, CircuitStateInfo>(_circuitStates);

    public IReadOnlyCollection<FileEventRecord> GetRecentEvents(int limit = 100) => [.. _events.Reverse().Take(limit)];

    public object GetStatus() {
        return new {
            ActiveWatchers = GetActiveWatchers(),
            RestartAttempts = GetRestartAttemptsSnapshot(),
            RecentEvents = GetRecentEvents(200),
            Timestamp = DateTimeOffset.Now,
            EventCount = _events.Count
        };
    }

    public IReadOnlyCollection<string> GetActiveWatchers() => _activeWatchers.Keys.ToList().AsReadOnly();
    public IReadOnlyDictionary<string, int> GetRestartAttemptsSnapshot() => new Dictionary<string, int>(_restartAttempts);

    /// <summary>
    /// Unregisters a watcher for the specified folder.
    /// </summary>
    /// <param name="folder">The folder path.</param>
    public void UnregisterWatcher(string folder) => _activeWatchers.TryRemove(folder, out _);

    /// <summary>
    /// Resets the restart attempt counter for the specified folder.
    /// </summary>
    /// <param name="folder">The folder path.</param>
    public void ResetRestart(string folder) => _restartAttempts.TryRemove(folder, out _);

    /// <summary>
    /// Registers a watcher for the specified folder.
    /// </summary>
    /// <param name="folder">The folder path.</param>
    public void RegisterWatcher(string folder) {
        if (!string.IsNullOrWhiteSpace(folder)) {
            _activeWatchers[folder] = 0;
        }
    }

    /// <summary>
    /// Increments the restart attempt count for the specified folder.
    /// </summary>
    /// <param name="folderPath">The folder path.</param>
    /// <returns>The updated restart attempt count.</returns>
    public int IncrementRestart(string folderPath) => _restartAttempts.AddOrUpdate(folderPath, 1, (_, count) => count + 1);

    /// <inheritdoc />
    public void Dispose() {
        // Stop and dispose resources safely; listeners may already be disposed/stopped.
        try {
            _cancellationTokenSource?.Cancel();
        }
        catch { }

        try {
            if (_httpListener is not null) {
                try { _httpListener.Stop(); } catch { }
                try { _httpListener.Close(); } catch { }
            }
        }
        catch { }

        try {
            _serverTask?.Wait(TimeSpan.FromSeconds(5));
        }
        catch { }

        try {
            _cancellationTokenSource?.Dispose();
        }
        catch { }
        GC.SuppressFinalize(this);
    }
}
