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

            if (config is not null)
            {
                // Decrypt the bearer token if it's encrypted
                if (!string.IsNullOrWhiteSpace(config.BearerToken))
                {
                    _logger.LogDebug("Checking bearer token encryption status. Token starts with 'enc:': {IsEncrypted}",
                        config.BearerToken.StartsWith("enc:"));

                    try
                    {
                        if (OperatingSystem.IsWindows() && SecureConfigurationHelper.IsTokenEncrypted(config.BearerToken))
                        {
                            _logger.LogInformation("Encrypted token detected, decrypting for runtime use");
                            // Create a copy for runtime use with decrypted token
                            var runtimeConfig = new ExternalConfiguration
                            {
                                Folders = config.Folders,
                                ApiEndpoint = config.ApiEndpoint,
                                BearerToken = SecureConfigurationHelper.DecryptBearerToken(config.BearerToken),
                                PostFileContents = config.PostFileContents,
                                ProcessedFolder = config.ProcessedFolder,
                                MoveProcessedFiles = config.MoveProcessedFiles,
                                AllowedExtensions = config.AllowedExtensions,
                                IncludeSubdirectories = config.IncludeSubdirectories,
                                DebounceMilliseconds = config.DebounceMilliseconds,
                                Retries = config.Retries,
                                RetryDelayMilliseconds = config.RetryDelayMilliseconds,
                                WatcherMaxRestartAttempts = config.WatcherMaxRestartAttempts,
                                WatcherRestartDelayMilliseconds = config.WatcherRestartDelayMilliseconds,
                                DiagnosticsUrlPrefix = config.DiagnosticsUrlPrefix,
                                ChannelCapacity = config.ChannelCapacity,
                                MaxParallelSends = config.MaxParallelSends,
                                FileWatcherInternalBufferSize = config.FileWatcherInternalBufferSize,
                                WaitForFileReadyMilliseconds = config.WaitForFileReadyMilliseconds
                            };

                            lock (_configLock)
                            {
                                _currentConfig = runtimeConfig;
                                return _currentConfig;
                            }
                        }
                        else if (OperatingSystem.IsWindows())
                        {
                            // Plain text token found - encrypt it and save back to file
                            _logger.LogInformation("Found plain text bearer token, encrypting for secure storage");

                            // Save the configuration with encrypted token
                            await SaveConfigurationAsync(config, cancellationToken);

                            // Return the runtime config with plain text token for use
                            lock (_configLock)
                            {
                                _currentConfig = config;
                                return _currentConfig;
                            }
                        }
                        else
                        {
                            _logger.LogWarning("Windows encryption not available - bearer token will remain in plain text");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to decrypt bearer token, treating as plain text");
                        // Continue with the original config if decryption fails
                    }
                }
            }

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
            // Create a copy for saving with encrypted bearer token
            var saveConfig = new ExternalConfiguration
            {
                Folders = config.Folders,
                ApiEndpoint = config.ApiEndpoint,
                BearerToken = OperatingSystem.IsWindows()
                    ? SecureConfigurationHelper.EnsureTokenIsEncrypted(config.BearerToken)
                    : config.BearerToken,
                PostFileContents = config.PostFileContents,
                ProcessedFolder = config.ProcessedFolder,
                MoveProcessedFiles = config.MoveProcessedFiles,
                AllowedExtensions = config.AllowedExtensions,
                IncludeSubdirectories = config.IncludeSubdirectories,
                DebounceMilliseconds = config.DebounceMilliseconds,
                Retries = config.Retries,
                RetryDelayMilliseconds = config.RetryDelayMilliseconds,
                WatcherMaxRestartAttempts = config.WatcherMaxRestartAttempts,
                WatcherRestartDelayMilliseconds = config.WatcherRestartDelayMilliseconds,
                DiagnosticsUrlPrefix = config.DiagnosticsUrlPrefix,
                ChannelCapacity = config.ChannelCapacity,
                MaxParallelSends = config.MaxParallelSends,
                FileWatcherInternalBufferSize = config.FileWatcherInternalBufferSize,
                WaitForFileReadyMilliseconds = config.WaitForFileReadyMilliseconds
            };

            var json = JsonSerializer.Serialize(saveConfig, MyJsonContext.Default.ExternalConfiguration);
            await File.WriteAllTextAsync(_configFilePath, json, cancellationToken);

            lock (_configLock)
            {
                _currentConfig = config; // Store the runtime config (with decrypted token)
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

            // Clear the cached config to force reload
            lock (_configLock)
            {
                _currentConfig = null;
            }

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
            WaitForFileReadyMilliseconds = 0,

            // Logging configuration
            LogLevel = "Information"
        };

        await SaveConfigurationAsync(defaultConfig, cancellationToken);
    }

    public void Dispose()
    {
        _configWatcher?.Dispose();
    }
}
