using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using System.Text;
using FileWatchRest.Models;

namespace FileWatchRest.Services;

public record FileEventRecord(string Path, DateTimeOffset Timestamp, bool PostedSuccess, int? StatusCode);

public class DiagnosticsService : IDisposable
{
    private readonly ConcurrentDictionary<string, int> _restartAttempts = new();
    private readonly ConcurrentDictionary<string, byte> _activeWatchers = new();
    private readonly ConcurrentQueue<FileEventRecord> _events = new();
    private readonly ILogger<DiagnosticsService> _logger;
    private HttpListener? _httpListener;
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _serverTask;

    public DiagnosticsService(ILogger<DiagnosticsService> logger)
    {
        _logger = logger;
    }

    public void StartHttpServer(string urlPrefix)
    {
        if (_httpListener != null)
            return; // Already started

        try
        {
            _logger.LogInformation("Attempting to start diagnostics HTTP server at {Url}", urlPrefix);

            _httpListener = new HttpListener();
            _httpListener.Prefixes.Add(urlPrefix);
            _httpListener.Start();

            _cancellationTokenSource = new CancellationTokenSource();
            _serverTask = Task.Run(() => HandleHttpRequests(_cancellationTokenSource.Token));

            _logger.LogInformation("Diagnostics HTTP server started successfully at {Url}", urlPrefix);
        }
        catch (HttpListenerException ex)
        {
            _logger.LogError(ex, "Failed to start diagnostics HTTP server at {Url}. Error code: {ErrorCode}. This may require administrator permissions or the URL may be reserved by another application.", urlPrefix, ex.ErrorCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start diagnostics HTTP server at {Url}", urlPrefix);
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
                _logger.LogWarning(ex, "Error in diagnostics HTTP server");
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
                            Timestamp = DateTimeOffset.UtcNow,
                            EventCount = _events.Count
                        };
                        responseText = JsonSerializer.Serialize(status, typeof(DiagnosticStatus), MyJsonContext.Default);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to serialize status response");
                        responseText = $"{{\"error\":\"Serialization failed\",\"message\":\"{ex.Message.Replace("\"", "\\\"")}\",\"timestamp\":\"{DateTimeOffset.UtcNow:O}\"}}";
                    }
                    break;

                case "/health":
                    try
                    {
                        var health = new HealthStatus
                        {
                            Status = "healthy",
                            Timestamp = DateTimeOffset.UtcNow
                        };
                        responseText = JsonSerializer.Serialize(health, typeof(HealthStatus), MyJsonContext.Default);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to serialize health response");
                        responseText = $"{{\"status\":\"healthy\",\"timestamp\":\"{DateTimeOffset.UtcNow:O}\",\"serializationError\":\"{ex.Message.Replace("\"", "\\\"")}\"}}";
                    }
                    break;

                case "/test":
                    responseText = "{\"message\":\"Hello from FileWatchRest diagnostics\",\"timestamp\":\"" + DateTimeOffset.UtcNow.ToString("O") + "\"}";
                    break;

                case "/events":
                    var events = GetRecentEvents(500).ToArray();
                    responseText = JsonSerializer.Serialize(events, typeof(FileEventRecord[]), MyJsonContext.Default);
                    break;

                case "/watchers":
                    var watchers = GetActiveWatchers().ToArray();
                    responseText = JsonSerializer.Serialize(watchers, typeof(string[]), MyJsonContext.Default);
                    break;

                default:
                    response.StatusCode = 404;
                    var error = new ErrorResponse
                    {
                        Error = "Not found",
                        AvailableEndpoints = new[] { "/status", "/health", "/events", "/watchers", "/test" }
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
            _logger.LogError(ex, "Error processing diagnostics request for path: {Path}", context.Request.Url?.AbsolutePath ?? "unknown");
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
                _logger.LogError(innerEx, "Failed to send error response");
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

    public void RecordFileEvent(string path, bool postedSuccess, int? statusCode)
    {
        _events.Enqueue(new FileEventRecord(path, DateTimeOffset.UtcNow, postedSuccess, statusCode));
        // keep the queue bounded to e.g. 1000 entries
        while (_events.Count > 1000 && _events.TryDequeue(out _)) { }
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
            Timestamp = DateTimeOffset.UtcNow,
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
            _logger.LogWarning(ex, "Error disposing diagnostics service");
        }
    }
}
