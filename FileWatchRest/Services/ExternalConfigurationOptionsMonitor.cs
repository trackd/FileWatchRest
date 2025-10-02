namespace FileWatchRest.Services;

/// <summary>
/// Wraps <see cref="ConfigurationService"/> and exposes the external configuration via <see cref="IOptionsMonitor{ExternalConfiguration}"/>.
/// This class loads the initial value synchronously during construction and registers for file-change notifications so OnChange callbacks will be invoked.
/// </summary>
public class ExternalConfigurationOptionsMonitor : IOptionsMonitor<ExternalConfiguration>
{
    private readonly ConfigurationService _configService;
    private readonly ILogger<ExternalConfigurationOptionsMonitor> _logger;
    private ExternalConfiguration _current;
    private readonly List<Action<ExternalConfiguration, string?>> _listeners = new();
    private readonly object _sync = new();

    private static readonly Action<ILogger<ExternalConfigurationOptionsMonitor>, Exception?> _failedToLoadInitial =
        LoggerMessage.Define(LogLevel.Warning, new EventId(1, "FailedToLoadInitial"), "Failed to load external configuration during options monitor initialization; using defaults");
    private static readonly Action<ILogger<ExternalConfigurationOptionsMonitor>, Exception?> _errorHandlingConfigChange =
        LoggerMessage.Define(LogLevel.Warning, new EventId(2, "ErrorHandlingConfigChange"), "Error handling external configuration change");
    private static readonly Action<ILogger<ExternalConfigurationOptionsMonitor>, Exception?> _failedToStartWatcher =
        LoggerMessage.Define(LogLevel.Warning, new EventId(3, "FailedToStartWatcher"), "Failed to start configuration watcher in ExternalConfigurationOptionsMonitor");
    private static readonly Action<ILogger<ExternalConfigurationOptionsMonitor>, Exception?> _listenerThrew =
        LoggerMessage.Define(LogLevel.Warning, new EventId(4, "ListenerThrew"), "Listener threw while handling configuration change");

    public ExternalConfigurationOptionsMonitor(ConfigurationService configService, ILogger<ExternalConfigurationOptionsMonitor> logger)
    {
        _configService = configService;
        _logger = logger;

        // Load initial config synchronously at startup (caller ensures this is appropriate during host boot)
        try
        {
            _current = _configService.LoadConfigurationAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _failedToLoadInitial(_logger, ex);
            _current = new ExternalConfiguration();
        }

        // Register a single watcher with the configuration service so we can notify IOptionsMonitor listeners when changes occur.
        try
        {
            _configService.StartWatching(async (newConfig) =>
            {
                try
                {
                    // Update current value and notify listeners
                    lock (_sync) { _current = newConfig; }
                    NotifyListeners(newConfig);
                }
                catch (Exception ex)
                {
                    _errorHandlingConfigChange(_logger, ex);
                }
                await Task.CompletedTask;
            });
        }
        catch (Exception ex)
        {
            _failedToStartWatcher(_logger, ex);
        }
    }

    private void NotifyListeners(ExternalConfiguration newConfig)
    {
        Action<ExternalConfiguration, string?>[] copy;
        lock (_sync)
        {
            copy = _listeners.ToArray();
        }
        foreach (var cb in copy)
        {
            try
            {
                cb(newConfig, null);
            }
            catch (Exception ex)
            {
                _listenerThrew(_logger, ex);
            }
        }
    }

    public ExternalConfiguration CurrentValue => _current;

    public ExternalConfiguration Get(string? name) => _current;

    public IDisposable OnChange(Action<ExternalConfiguration, string?> listener)
    {
        lock (_sync)
        {
            _listeners.Add(listener);
        }
        return new DisposableAction(() => { lock (_sync) { _listeners.Remove(listener); } });
    }

    private sealed class DisposableAction : IDisposable
    {
        private readonly Action _action;
        private int _disposed;
        public DisposableAction(Action action) => _action = action;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) _action();
        }
    }
}
