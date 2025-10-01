namespace FileWatchRest.Configuration;

/// <summary>
/// Service responsible for managing external configuration stored in AppData
/// </summary>
public class ConfigurationService : IDisposable
{
    private readonly ILogger<ConfigurationService> _logger;
    private readonly string _configFilePath;
    private FileSystemWatcher? _configWatcher;
    private ExternalConfiguration? _currentConfig;
    private readonly Lock _configLock = new();

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
            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("Configuration file not found at {Path}, creating default configuration", _configFilePath);
            }
            await CreateDefaultConfigurationAsync(cancellationToken);
        }

        try
        {
            var json = await File.ReadAllTextAsync(_configFilePath, cancellationToken);
            bool migrated = false;
            JsonDocument? doc = null;
            try
            {
                doc = JsonDocument.Parse(json);
            }
            catch
            {
                // If parsing fails, we'll still attempt to deserialize with the typed context below
                doc = null;
            }
            ExternalConfiguration? config = null;
            try
            {
                config = JsonSerializer.Deserialize(json, MyJsonContext.Default.ExternalConfiguration);
            }
            catch
            {
                // Fall back to manual construction from JsonDocument (avoid runtime converters)
                if (doc is not null)
                {
                    try
                    {
                        var manual = new ExternalConfiguration();
                        if (doc.RootElement.TryGetProperty("Folders", out var foldersElem) && foldersElem.ValueKind == JsonValueKind.Array)
                        {
                            var list = new List<string>();
                            foreach (var it in foldersElem.EnumerateArray())
                            {
                                if (it.ValueKind == JsonValueKind.String) list.Add(it.GetString()!);
                            }
                            manual.Folders = list.ToArray();
                        }
                        if (doc.RootElement.TryGetProperty("ApiEndpoint", out var apiElem) && apiElem.ValueKind == JsonValueKind.String)
                            manual.ApiEndpoint = apiElem.GetString();
                        if (doc.RootElement.TryGetProperty("ProcessedFolder", out var pf) && pf.ValueKind == JsonValueKind.String)
                            manual.ProcessedFolder = pf.GetString()!;
                        if (doc.RootElement.TryGetProperty("DiagnosticsUrlPrefix", out var durl) && durl.ValueKind == JsonValueKind.String)
                            manual.DiagnosticsUrlPrefix = durl.GetString()!;
                        if (doc.RootElement.TryGetProperty("AllowedExtensions", out var extElem) && extElem.ValueKind == JsonValueKind.Array)
                        {
                            var exts = new List<string>();
                            foreach (var it in extElem.EnumerateArray()) if (it.ValueKind == JsonValueKind.String) exts.Add(it.GetString()!);
                            manual.AllowedExtensions = exts.ToArray();
                        }
                        if (doc.RootElement.TryGetProperty("IncludeSubdirectories", out var inc) && inc.ValueKind == JsonValueKind.True || inc.ValueKind == JsonValueKind.False)
                            manual.IncludeSubdirectories = inc.GetBoolean();
                        if (doc.RootElement.TryGetProperty("DebounceMilliseconds", out var dm) && dm.ValueKind == JsonValueKind.Number && dm.TryGetInt32(out var dmv)) manual.DebounceMilliseconds = dmv;
                        if (doc.RootElement.TryGetProperty("Retries", out var r) && r.ValueKind == JsonValueKind.Number && r.TryGetInt32(out var rv)) manual.Retries = rv;
                        if (doc.RootElement.TryGetProperty("RetryDelayMilliseconds", out var rd) && rd.ValueKind == JsonValueKind.Number && rd.TryGetInt32(out var rdv)) manual.RetryDelayMilliseconds = rdv;
                        if (doc.RootElement.TryGetProperty("WatcherMaxRestartAttempts", out var wr) && wr.ValueKind == JsonValueKind.Number && wr.TryGetInt32(out var wrv)) manual.WatcherMaxRestartAttempts = wrv;
                        if (doc.RootElement.TryGetProperty("WatcherRestartDelayMilliseconds", out var wdr) && wdr.ValueKind == JsonValueKind.Number && wdr.TryGetInt32(out var wdrv)) manual.WatcherRestartDelayMilliseconds = wdrv;
                        if (doc.RootElement.TryGetProperty("ChannelCapacity", out var cc) && cc.ValueKind == JsonValueKind.Number && cc.TryGetInt32(out var ccv)) manual.ChannelCapacity = ccv;
                        if (doc.RootElement.TryGetProperty("MaxParallelSends", out var mps) && mps.ValueKind == JsonValueKind.Number && mps.TryGetInt32(out var mpsv)) manual.MaxParallelSends = mpsv;
                        if (doc.RootElement.TryGetProperty("FileWatcherInternalBufferSize", out var fb) && fb.ValueKind == JsonValueKind.Number && fb.TryGetInt32(out var fbv)) manual.FileWatcherInternalBufferSize = fbv;
                        if (doc.RootElement.TryGetProperty("WaitForFileReadyMilliseconds", out var wf) && wf.ValueKind == JsonValueKind.Number && wf.TryGetInt32(out var wfv)) manual.WaitForFileReadyMilliseconds = wfv;
                        if (doc.RootElement.TryGetProperty("MaxContentBytes", out var mcb) && mcb.ValueKind == JsonValueKind.Number && mcb.TryGetInt64(out var mcbv)) manual.MaxContentBytes = mcbv;
                        if (doc.RootElement.TryGetProperty("StreamingThresholdBytes", out var stb) && stb.ValueKind == JsonValueKind.Number && stb.TryGetInt64(out var stbv)) manual.StreamingThresholdBytes = stbv;
                        if (doc.RootElement.TryGetProperty("EnableCircuitBreaker", out var ecb) && (ecb.ValueKind == JsonValueKind.True || ecb.ValueKind == JsonValueKind.False)) manual.EnableCircuitBreaker = ecb.GetBoolean();
                        if (doc.RootElement.TryGetProperty("CircuitBreakerFailureThreshold", out var cbft) && cbft.ValueKind == JsonValueKind.Number && cbft.TryGetInt32(out var cbftv)) manual.CircuitBreakerFailureThreshold = cbftv;
                        if (doc.RootElement.TryGetProperty("CircuitBreakerOpenDurationMilliseconds", out var co) && co.ValueKind == JsonValueKind.Number && co.TryGetInt32(out var cov)) manual.CircuitBreakerOpenDurationMilliseconds = cov;
                        if (doc.RootElement.TryGetProperty("PostFileContents", out var pfc) && (pfc.ValueKind == JsonValueKind.True || pfc.ValueKind == JsonValueKind.False)) manual.PostFileContents = pfc.GetBoolean();
                        if (doc.RootElement.TryGetProperty("MoveProcessedFiles", out var mpf) && (mpf.ValueKind == JsonValueKind.True || mpf.ValueKind == JsonValueKind.False)) manual.MoveProcessedFiles = mpf.GetBoolean();
                        if (doc.RootElement.TryGetProperty("BearerToken", out var bt) && bt.ValueKind == JsonValueKind.String) manual.BearerToken = bt.GetString();

                        // Logging section
                        if (doc.RootElement.TryGetProperty("Logging", out var loggingElem) && loggingElem.ValueKind == JsonValueKind.Object)
                        {
                            var lo = new LoggingOptions();
                            if (loggingElem.TryGetProperty("LogType", out var ltype) && ltype.ValueKind == JsonValueKind.String)
                            {
                                if (Enum.TryParse<LogType>(ltype.GetString(), true, out var lt)) lo.LogType = lt;
                            }
                            if (loggingElem.TryGetProperty("FilePathPattern", out var fpp) && fpp.ValueKind == JsonValueKind.String) lo.FilePathPattern = fpp.GetString()!;
                            if (loggingElem.TryGetProperty("LogLevel", out var ll) && ll.ValueKind == JsonValueKind.String) lo.LogLevel = ll.GetString()!;
                            if (loggingElem.TryGetProperty("RetainedFileCountLimit", out var rfl) && rfl.ValueKind == JsonValueKind.Number && rfl.TryGetInt32(out var rflv)) lo.RetainedFileCountLimit = rflv;
                            manual.Logging = lo;
                        }

                        config = manual;
                    }
                    catch
                    {
                        config = null;
                    }
                }
            }

            // Run lightweight migration for older schemas:
            try
            {
                if (doc is not null && config is not null)
                {
                    // Ensure Logging object exists
                    config.Logging ??= new LoggingOptions();

                    var migratedReasons = new List<string>();

                    // 1) Migrate top-level LogLevel (case-insensitive)
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        if (string.Equals(prop.Name, "LogLevel", StringComparison.OrdinalIgnoreCase))
                        {
                            var topVal = prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() : null;
                            if (!string.IsNullOrWhiteSpace(topVal) && !string.Equals(config.Logging.LogLevel, topVal, StringComparison.OrdinalIgnoreCase))
                            {
                                migrated = true;
                                migratedReasons.Add($"TopLevel LogLevel -> Logging.LogLevel: {topVal}");
                                config.Logging.LogLevel = topVal!;
                            }
                            break;
                        }
                    }

                    // 2) Migrate Logging.MinimumLevel (legacy) to Logging.LogLevel
                    if (doc.RootElement.TryGetProperty("Logging", out var loggingElemRaw) && loggingElemRaw.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var p in loggingElemRaw.EnumerateObject())
                        {
                            if (string.Equals(p.Name, "MinimumLevel", StringComparison.OrdinalIgnoreCase))
                            {
                                var minVal = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : null;
                                if (!string.IsNullOrWhiteSpace(minVal) && !string.Equals(config.Logging.LogLevel, minVal, StringComparison.OrdinalIgnoreCase))
                                {
                                    migrated = true;
                                    migratedReasons.Add($"Logging.MinimumLevel -> Logging.LogLevel: {minVal}");
                                    config.Logging.LogLevel = minVal!;
                                }
                                break;
                            }
                        }
                    }

                    if (migrated)
                    {
                        if (_logger.IsEnabled(LogLevel.Information))
                        {
                            _logger.LogInformation("Migrated legacy logging settings to Logging.LogLevel for configuration at {Path}", _configFilePath);
                        }
                        // Persist the migrated configuration back to disk (this will also ensure tokens are encrypted)
                        await SaveConfigurationAsync(config, cancellationToken);

                        // Write an audit record (best-effort) containing timestamp and what changed
                        try
                        {
                            var dir = Path.GetDirectoryName(_configFilePath) ?? AppContext.BaseDirectory;
                            var auditPath = Path.Combine(dir, "migration-audit.log");
                            var auditLine = $"{DateTime.Now:O} - Migrated: {string.Join("; ", migratedReasons)}{Environment.NewLine}";
                            File.AppendAllText(auditPath, auditLine);
                        }
                        catch
                        {
                            // Ignore audit write failures - migration already persisted
                        }

                        // Keep the modified 'config' instance as the canonical runtime configuration
                    }
                }
            }
            catch (Exception ex)
            {
                if (_logger.IsEnabled(LogLevel.Warning))
                {
                    _logger.LogWarning(ex, "Logging migration attempt failed for {Path} - proceeding with deserialized config", _configFilePath);
                }
            }

            // Validate the deserialized configuration early to avoid loading invalid settings.
            try
            {
                if (config is null)
                {
                    if (_logger.IsEnabled(LogLevel.Warning))
                    {
                        _logger.LogWarning("Configuration file {Path} could not be deserialized; using defaults", _configFilePath);
                    }
                    lock (_configLock)
                    {
                        _currentConfig = new ExternalConfiguration();
                        return _currentConfig;
                    }
                }
                 var validator = new ExternalConfigurationValidator();
                 var validationResult = validator.Validate(config);
                 if (!validationResult.IsValid)
                 {
                     if (_logger.IsEnabled(LogLevel.Error))
                     {
                         _logger.LogError("Loaded configuration is invalid: {Errors}", string.Join("; ", validationResult.Errors.Select(e => e.ErrorMessage)));
                     }
                     lock (_configLock)
                     {
                         _currentConfig = new ExternalConfiguration();
                         return _currentConfig;
                     }
                 }
             }
             catch (Exception ex)
             {
                 _logger.LogWarning(ex, "Configuration validation threw an exception; proceeding with loaded configuration");
             }
             // Decrypt the bearer token if it's encrypted
            if (config is not null && !string.IsNullOrWhiteSpace(config.BearerToken))
             {
                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug("Checking bearer token encryption status. Token starts with 'enc:': {IsEncrypted}",
                        config.BearerToken.StartsWith("enc:"));
                }

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
                            WaitForFileReadyMilliseconds = config.WaitForFileReadyMilliseconds,
                            MaxContentBytes = config.MaxContentBytes,
                            StreamingThresholdBytes = config.StreamingThresholdBytes,
                            EnableCircuitBreaker = config.EnableCircuitBreaker,
                            CircuitBreakerFailureThreshold = config.CircuitBreakerFailureThreshold,
                            CircuitBreakerOpenDurationMilliseconds = config.CircuitBreakerOpenDurationMilliseconds,
                            // Preserve logging section during runtime config creation
                            Logging = config.Logging ?? new LoggingOptions()
                        };

                        // Validate runtime configuration (after decrypt) before returning
                        try
                        {
                            var runtimeValidator = new ExternalConfigurationValidator();
                            var runtimeValidation = runtimeValidator.Validate(runtimeConfig);
                            if (!runtimeValidation.IsValid)
                            {
                                if (_logger.IsEnabled(LogLevel.Error))
                                {
                                    _logger.LogError("Runtime configuration (after decryption) is invalid: {Errors}", string.Join("; ", runtimeValidation.Errors.Select(e => e.ErrorMessage)));
                                }
                                lock (_configLock)
                                {
                                    _currentConfig = new ExternalConfiguration();
                                    return _currentConfig;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Runtime configuration validation threw an exception; proceeding with decrypted configuration");
                        }

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

            // Final validation before accepting the loaded configuration (fallback to defaults on failure)
            if (config is not null)
            {
                try
                {
                    var finalValidator = new ExternalConfigurationValidator();
                    var finalResult = finalValidator.Validate(config);
                    if (!finalResult.IsValid)
                    {
                        if (_logger.IsEnabled(LogLevel.Error))
                        {
                            _logger.LogError("Final configuration validation failed: {Errors}", string.Join("; ", finalResult.Errors.Select(e => e.ErrorMessage)));
                        }
                        lock (_configLock)
                        {
                            _currentConfig = new ExternalConfiguration();
                            return _currentConfig;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Final configuration validation threw an exception; proceeding with loaded configuration");
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
            if (_logger.IsEnabled(LogLevel.Error))
            {
                _logger.LogError(ex, "Failed to load configuration from {Path}, using default", _configFilePath);
            }
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
                WaitForFileReadyMilliseconds = config.WaitForFileReadyMilliseconds,
                Logging = config.Logging
             };

            var json = JsonSerializer.Serialize(saveConfig, MyJsonContext.Default.ExternalConfiguration);
            await File.WriteAllTextAsync(_configFilePath, json, cancellationToken);

            lock (_configLock)
            {
                _currentConfig = config; // Store the runtime config (with decrypted token)
            }

            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("Configuration saved to {Path}", _configFilePath);
            }
        }
        catch (Exception ex)
        {
            if (_logger.IsEnabled(LogLevel.Error))
            {
                _logger.LogError(ex, "Failed to save configuration to {Path}", _configFilePath);
            }
            throw;
        }
    }

    public void StartWatching(Func<ExternalConfiguration, Task> onConfigChanged)
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

            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("Started watching configuration file {Path}", _configFilePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to start watching configuration file");
        }
    }

    private async Task OnConfigFileChanged(Func<ExternalConfiguration, Task> onConfigChanged)
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
            await onConfigChanged(newConfig);

            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("Configuration reloaded from {Path}", _configFilePath);
            }
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
            MaxContentBytes = 5 * 1024 * 1024, // default 5 MB
            // Streaming threshold for large files (stream when > threshold)
            StreamingThresholdBytes = 256 * 1024, // 256 KB

            // Circuit breaker default: disabled
            EnableCircuitBreaker = false,
            CircuitBreakerFailureThreshold = 5,
            CircuitBreakerOpenDurationMilliseconds = 30_000,

            // Logging configuration - use nested LogLevel
            Logging = new LoggingOptions
            {
                LogType = LogType.Csv,
                FilePathPattern = "logs/FileWatchRest_{0:yyyyMMdd_HHmmss}",
                UseJsonFile = false, // JSON is opt-in
                JsonFilePath = "logs/FileWatchRest_{0:yyyyMMdd_HHmmss}.ndjson",
                UseCsvFile = true,   // CSV enabled by default
                CsvFilePath = "logs/FileWatchRest_{0:yyyyMMdd_HHmmss}.csv",
                LogLevel = "Information",
                RetainedFileCountLimit = 14
            }
        };

        await SaveConfigurationAsync(defaultConfig, cancellationToken);
    }

    public void Dispose()
    {
        _configWatcher?.Dispose();
    }

    public static ExternalConfiguration? LoadFromFile(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize(json, MyJsonContext.Default.ExternalConfiguration);
        }
        catch
        {
            return null;
        }
    }
}
