using System.Text.Json;
using FileWatchRest.Models;

namespace FileWatchRest.Configuration;

/// <summary>
/// Service responsible for managing external configuration stored in AppData
/// </summary>
public class ConfigurationService
{
    private readonly ILogger<ConfigurationService> _logger;
    private readonly string _configFilePath;
    private FileSystemWatcher? _configWatcher;
    private ExternalConfiguration? _currentConfig;
    private readonly object _configLock = new();

    public ConfigurationService(ILogger<ConfigurationService> logger, string serviceName)
    {
        _logger = logger;
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var serviceDir = Path.Combine(appDataPath, serviceName);
        Directory.CreateDirectory(serviceDir);
        _configFilePath = Path.Combine(serviceDir, "FileWatchRest.json");
    }

    public async Task<ExternalConfiguration> LoadConfigurationAsync(CancellationToken cancellationToken = default)
    {
        lock (_configLock)
        {
            if (_currentConfig is not null)
                return _currentConfig;
        }

        if (!File.Exists(_configFilePath))
        {
            _logger.LogInformation("Configuration file not found at {Path}, creating default configuration", _configFilePath);
            await CreateDefaultConfigurationAsync(cancellationToken);
        }

        try
        {
            var json = await File.ReadAllTextAsync(_configFilePath, cancellationToken);
            var config = JsonSerializer.Deserialize(json, MyJsonContext.Default.ExternalConfiguration);

            lock (_configLock)
            {
                _currentConfig = config ?? new ExternalConfiguration();
                return _currentConfig;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load configuration from {Path}, using default", _configFilePath);
            lock (_configLock)
            {
                _currentConfig = new ExternalConfiguration();
                return _currentConfig;
            }
        }
    }

    public async Task SaveConfigurationAsync(ExternalConfiguration config, CancellationToken cancellationToken = default)
    {
        try
        {
            var json = JsonSerializer.Serialize(config, MyJsonContext.Default.ExternalConfiguration);
            await File.WriteAllTextAsync(_configFilePath, json, cancellationToken);

            lock (_configLock)
            {
                _currentConfig = config;
            }

            _logger.LogInformation("Configuration saved to {Path}", _configFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save configuration to {Path}", _configFilePath);
            throw;
        }
    }

    public void StartWatching(Action<ExternalConfiguration> onConfigChanged)
    {
        try
        {
            var directory = Path.GetDirectoryName(_configFilePath)!;
            var fileName = Path.GetFileName(_configFilePath);

            _configWatcher = new FileSystemWatcher(directory)
            {
                Filter = fileName,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime
            };

            _configWatcher.Changed += async (_, _) => await OnConfigFileChanged(onConfigChanged);
            _configWatcher.Created += async (_, _) => await OnConfigFileChanged(onConfigChanged);
            _configWatcher.EnableRaisingEvents = true;

            _logger.LogInformation("Started watching configuration file {Path}", _configFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to start watching configuration file");
        }
    }

    private async Task OnConfigFileChanged(Action<ExternalConfiguration> onConfigChanged)
    {
        try
        {
            // Small delay to avoid reading while file is being written
            await Task.Delay(250);

            var newConfig = await LoadConfigurationAsync();
            onConfigChanged(newConfig);

            _logger.LogInformation("Configuration reloaded from {Path}", _configFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to reload configuration after file change");
        }
    }

    private async Task CreateDefaultConfigurationAsync(CancellationToken cancellationToken)
    {
        var defaultConfig = new ExternalConfiguration
        {
            // Core file watching settings
            Folders = [@"C:\temp\watch"],
            ApiEndpoint = "http://localhost:8080/api/files",
            PostFileContents = false,
            MoveProcessedFiles = false,
            ProcessedFolder = "processed",
            AllowedExtensions = [".txt", ".json", ".xml"],
            IncludeSubdirectories = true,
            DebounceMilliseconds = 1000,

            // Performance and reliability settings
            Retries = 3,
            RetryDelayMilliseconds = 500,
            WatcherMaxRestartAttempts = 3,
            WatcherRestartDelayMilliseconds = 1000,
            DiagnosticsUrlPrefix = "http://localhost:5005/",
            ChannelCapacity = 1000,
            MaxParallelSends = 4,
            FileWatcherInternalBufferSize = 64 * 1024,
            WaitForFileReadyMilliseconds = 0
        };

        await SaveConfigurationAsync(defaultConfig, cancellationToken);
    }

    public void Dispose()
    {
        _configWatcher?.Dispose();
    }
}
