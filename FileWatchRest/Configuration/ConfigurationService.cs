namespace FileWatchRest.Configuration;

/// <summary>
/// Service responsible for managing external configuration stored in ProgramData.
///
/// Architecture:
/// - Loads/saves JSON configuration from ProgramData\FileWatchRest\FileWatchRest.json
/// - Automatically encrypts bearer tokens on Windows using DPAPI
/// - Validates configuration on load
/// - Provides file-watching capability for configuration changes
/// - Integrates with IOptionsMonitor via ExternalConfigurationOptionsMonitor
///
/// The service handles:
/// - Token encryption/decryption (Windows only)
/// - Configuration migration from legacy formats
/// - Default configuration creation
/// - Change notification through file watcher
/// </summary>
public class ConfigurationService : IDisposable
{
    /// <summary>
    /// Gets the path to the configuration file.
    /// </summary>
    public string ConfigFilePath => _configFilePath;
    private static readonly Action<ILogger<ConfigurationService>, string, Exception?> _loadedExcludePatterns =
    LoggerMessage.Define<string>(LogLevel.Information, new EventId(101, "LoadedExcludePatterns"), "Loaded exclude patterns: {Patterns}");
    private static readonly Action<ILogger<ConfigurationService>, string, Exception?> _configFileNotFound =
        LoggerMessage.Define<string>(LogLevel.Information, new EventId(1, "ConfigFileNotFound"), "Configuration file not found at {Path}, creating default configuration");
    private static readonly Action<ILogger<ConfigurationService>, string, Exception?> _migratedLoggingSettings =
        LoggerMessage.Define<string>(LogLevel.Information, new EventId(2, "MigratedLoggingSettings"), "Migrated legacy logging settings to Logging.LogLevel for configuration at {Path}");
    private static readonly Action<ILogger<ConfigurationService>, string, Exception?> _loggingMigrationFailed =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(3, "LoggingMigrationFailed"), "Logging migration attempt failed for {Path} - proceeding with deserialized config");
    private static readonly Action<ILogger<ConfigurationService>, string, Exception?> _configDeserializationFailed =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(4, "ConfigDeserializationFailed"), "Configuration file {Path} could not be deserialized; using defaults");
    private static readonly Action<ILogger<ConfigurationService>, string, Exception?> _loadedConfigurationInvalid =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(5, "LoadedConfigurationInvalid"), "Loaded configuration is invalid: {Errors}");
    private static readonly Action<ILogger<ConfigurationService>, Exception?> _configValidationWarning =
        LoggerMessage.Define(LogLevel.Warning, new EventId(6, "ConfigValidationWarning"), "Configuration validation threw an exception; proceeding with loaded configuration");
    private static readonly Action<ILogger<ConfigurationService>, bool, Exception?> _checkingTokenEncryption =
        LoggerMessage.Define<bool>(LogLevel.Debug, new EventId(7, "CheckingTokenEncryption"), "Checking bearer token encryption status. Token starts with 'enc:': {IsEncrypted}");
    private static readonly Action<ILogger<ConfigurationService>, Exception?> _decryptingToken =
        LoggerMessage.Define(LogLevel.Information, new EventId(8, "DecryptingToken"), "Encrypted token detected, decrypting for runtime use");
    private static readonly Action<ILogger<ConfigurationService>, Exception?> _encryptingToken =
        LoggerMessage.Define(LogLevel.Information, new EventId(9, "EncryptingToken"), "Found plain text bearer token, encrypting for secure storage");
    private static readonly Action<ILogger<ConfigurationService>, string, Exception?> _failedToLoad =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(10, "FailedToLoad"), "Failed to load configuration from {Path}, using default");
    private static readonly Action<ILogger<ConfigurationService>, string, Exception?> _configSaved =
        LoggerMessage.Define<string>(LogLevel.Information, new EventId(11, "ConfigSaved"), "Configuration saved to {Path}");
    private static readonly Action<ILogger<ConfigurationService>, string, Exception?> _failedToSave =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(12, "FailedToSave"), "Failed to save configuration to {Path}");
    private static readonly Action<ILogger<ConfigurationService>, Exception?> _runtimeValidationWarning =
        LoggerMessage.Define(LogLevel.Warning, new EventId(19, "RuntimeConfigValidationWarning"), "Runtime configuration validation threw an exception; proceeding with decrypted configuration");
    private static readonly Action<ILogger<ConfigurationService>, Exception?> _encryptionNotAvailable =
        LoggerMessage.Define(LogLevel.Warning, new EventId(20, "EncryptionNotAvailable"), "Windows encryption not available - bearer token will remain in plain text");
    private static readonly Action<ILogger<ConfigurationService>, Exception?> _failedToDecryptToken =
        LoggerMessage.Define(LogLevel.Warning, new EventId(21, "FailedToDecryptToken"), "Failed to decrypt bearer token, treating as plain text");
    private static readonly Action<ILogger<ConfigurationService>, string, Exception?> _startedWatchingConfig =
        LoggerMessage.Define<string>(LogLevel.Information, new EventId(22, "StartedWatchingConfig"), "Started watching configuration file {Path}");
    private static readonly Action<ILogger<ConfigurationService>, Exception?> _failedToStartWatcher =
        LoggerMessage.Define(LogLevel.Warning, new EventId(23, "FailedToStartWatcher"), "Failed to start watching configuration file");
    private static readonly Action<ILogger<ConfigurationService>, string, Exception?> _configReloadedInfo =
        LoggerMessage.Define<string>(LogLevel.Information, new EventId(24, "ConfigReloaded"), "Configuration reloaded from {Path}");
    private static readonly Action<ILogger<ConfigurationService>, Exception?> _failedReloadWarning =
        LoggerMessage.Define(LogLevel.Warning, new EventId(25, "FailedReloadAfterChange"), "Failed to reload configuration after file change");
    private static readonly Action<ILogger<ConfigurationService>, string, Exception?> _autogenDiagnosticsToken =
        LoggerMessage.Define<string>(LogLevel.Information, new EventId(26, "AutogeneratedDiagnosticsToken"), "Generated diagnostics token: {Token}. The token will be persisted to the configuration file and encrypted on Windows.");

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
            _configFileNotFound(_logger, _configFilePath, null);
            await CreateDefaultConfigurationAsync(cancellationToken);
        }

        try
        {
            var json = await File.ReadAllTextAsync(_configFilePath, cancellationToken);
            bool migrated = false;
            JsonDocument? doc = null;
            var hadDiagnosticsTokenPropertyInJson = false;
            try
            {
                doc = JsonDocument.Parse(json);
                hadDiagnosticsTokenPropertyInJson = doc.RootElement.TryGetProperty("DiagnosticsBearerToken", out var _);
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
                if (config is not null && config.ExcludePatterns is { Length: > 0 })
                {
                    _loadedExcludePatterns(_logger, string.Join(", ", config.ExcludePatterns), null);
                }
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
                        if (doc.RootElement.TryGetProperty("ExcludePatterns", out var excludeElem) && excludeElem.ValueKind == JsonValueKind.Array)
                        {
                            var excludes = new List<string>();
                            foreach (var it in excludeElem.EnumerateArray()) if (it.ValueKind == JsonValueKind.String) excludes.Add(it.GetString()!);
                            manual.ExcludePatterns = excludes.ToArray();
                            if (manual.ExcludePatterns is { Length: > 0 })
                            {
                                _loadedExcludePatterns(_logger, string.Join(", ", manual.ExcludePatterns), null);
                            }
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
                        if (doc.RootElement.TryGetProperty("DiscardZeroByteFiles", out var dz) && (dz.ValueKind == JsonValueKind.True || dz.ValueKind == JsonValueKind.False)) manual.DiscardZeroByteFiles = dz.GetBoolean();
                        if (doc.RootElement.TryGetProperty("EnableCircuitBreaker", out var ecb) && (ecb.ValueKind == JsonValueKind.True || ecb.ValueKind == JsonValueKind.False)) manual.EnableCircuitBreaker = ecb.GetBoolean();
                        if (doc.RootElement.TryGetProperty("CircuitBreakerFailureThreshold", out var cbft) && cbft.ValueKind == JsonValueKind.Number && cbft.TryGetInt32(out var cbftv)) manual.CircuitBreakerFailureThreshold = cbftv;
                        if (doc.RootElement.TryGetProperty("CircuitBreakerOpenDurationMilliseconds", out var co) && co.ValueKind == JsonValueKind.Number && co.TryGetInt32(out var cov)) manual.CircuitBreakerOpenDurationMilliseconds = cov;
                        if (doc.RootElement.TryGetProperty("PostFileContents", out var pfc) && (pfc.ValueKind == JsonValueKind.True || pfc.ValueKind == JsonValueKind.False)) manual.PostFileContents = pfc.GetBoolean();
                        if (doc.RootElement.TryGetProperty("MoveProcessedFiles", out var mpf) && (mpf.ValueKind == JsonValueKind.True || mpf.ValueKind == JsonValueKind.False)) manual.MoveProcessedFiles = mpf.GetBoolean();
                        if (doc.RootElement.TryGetProperty("BearerToken", out var bt) && bt.ValueKind == JsonValueKind.String) manual.BearerToken = bt.GetString();
                        if (doc.RootElement.TryGetProperty("DiagnosticsBearerToken", out var dbt) && dbt.ValueKind == JsonValueKind.String) manual.DiagnosticsBearerToken = dbt.GetString();

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
                        _migratedLoggingSettings(_logger, _configFilePath, null);
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
                _loggingMigrationFailed(_logger, _configFilePath, ex);
            }

            // Validate the deserialized configuration early to avoid loading invalid settings.
            try
            {
                if (config is null)
                {
                    _configDeserializationFailed(_logger, _configFilePath, null);
                    lock (_configLock)
                    {
                        _currentConfig = new ExternalConfiguration();
                        return _currentConfig;
                    }
                }
                var validationResult = ExternalConfigurationValidator.Validate(config);
                if (!validationResult.IsValid)
                {
                    _loadedConfigurationInvalid(_logger, string.Join("; ", validationResult.Errors.Select(e => e.ErrorMessage)), null);
                    lock (_configLock)
                    {
                        _currentConfig = new ExternalConfiguration();
                        return _currentConfig;
                    }
                }
            }
            catch (Exception ex)
            {
                _configValidationWarning(_logger, ex);
            }
            // Decrypt/encrypt any bearer tokens (API and Diagnostics) as needed
            if (config is not null)
            {
                var hasApiToken = !string.IsNullOrWhiteSpace(config.BearerToken);
                var hasDiagToken = !string.IsNullOrWhiteSpace(config.DiagnosticsBearerToken);

                if (hasApiToken) _checkingTokenEncryption(_logger, config.BearerToken!.StartsWith("enc:"), null);
                if (hasDiagToken) _checkingTokenEncryption(_logger, config.DiagnosticsBearerToken!.StartsWith("enc:"), null);

                try
                {
                    // If any token is encrypted, decrypt the encrypted ones for runtime use
                    if (OperatingSystem.IsWindows() &&
                        ((hasApiToken && SecureConfigurationHelper.IsTokenEncrypted(config.BearerToken)) || (hasDiagToken && SecureConfigurationHelper.IsTokenEncrypted(config.DiagnosticsBearerToken))))
                    {
                        _decryptingToken(_logger, null);

                        var runtimeConfig = new ExternalConfiguration
                        {
                            Folders = config.Folders,
                            ApiEndpoint = config.ApiEndpoint,
                            BearerToken = hasApiToken && SecureConfigurationHelper.IsTokenEncrypted(config.BearerToken)
                                ? SecureConfigurationHelper.DecryptBearerToken(config.BearerToken!)
                                : config.BearerToken,
                            PostFileContents = config.PostFileContents,
                            ProcessedFolder = config.ProcessedFolder,
                            MoveProcessedFiles = config.MoveProcessedFiles,
                            AllowedExtensions = config.AllowedExtensions,
                            ExcludePatterns = config.ExcludePatterns,
                            IncludeSubdirectories = config.IncludeSubdirectories,
                            DebounceMilliseconds = config.DebounceMilliseconds,
                            Retries = config.Retries,
                            RetryDelayMilliseconds = config.RetryDelayMilliseconds,
                            WatcherMaxRestartAttempts = config.WatcherMaxRestartAttempts,
                            WatcherRestartDelayMilliseconds = config.WatcherRestartDelayMilliseconds,
                            DiagnosticsUrlPrefix = config.DiagnosticsUrlPrefix,
                            DiagnosticsBearerToken = hasDiagToken && SecureConfigurationHelper.IsTokenEncrypted(config.DiagnosticsBearerToken)
                                ? SecureConfigurationHelper.DecryptBearerToken(config.DiagnosticsBearerToken!)
                                : config.DiagnosticsBearerToken,
                            DiscardZeroByteFiles = config.DiscardZeroByteFiles,
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
                            var runtimeValidation = ExternalConfigurationValidator.Validate(runtimeConfig);
                            if (!runtimeValidation.IsValid)
                            {
                                _loadedConfigurationInvalid(_logger, string.Join("; ", runtimeValidation.Errors.Select(e => e.ErrorMessage)), null);
                            }
                        }
                        catch (Exception ex)
                        {
                            _runtimeValidationWarning(_logger, ex);
                        }

                        lock (_configLock)
                        {
                            _currentConfig = runtimeConfig;
                            return _currentConfig;
                        }
                    }

                    // If tokens exist in plain text and encryption is available, encrypt and persist them
                    if (OperatingSystem.IsWindows() && (hasApiToken || hasDiagToken))
                    {
                        // If any plain token exists, encrypt and save
                        if ((hasApiToken && !SecureConfigurationHelper.IsTokenEncrypted(config.BearerToken)) || (hasDiagToken && !SecureConfigurationHelper.IsTokenEncrypted(config.DiagnosticsBearerToken)))
                        {
                            // If diagnostics token wasn't provided in the JSON (i.e. it was auto-generated by the runtime),
                            // log it once here so operators can find it; the token will then be encrypted on save on Windows.
                            if (!hadDiagnosticsTokenPropertyInJson && hasDiagToken && !string.IsNullOrWhiteSpace(config.DiagnosticsBearerToken))
                            {
                                _autogenDiagnosticsToken(_logger, config.DiagnosticsBearerToken!, null);
                            }
                            _encryptingToken(_logger, null);
                            await SaveConfigurationAsync(config, cancellationToken);

                            // Return the runtime config with plain text tokens for runtime use
                            lock (_configLock)
                            {
                                _currentConfig = config;
                                return _currentConfig;
                            }
                        }
                    }
                    else if (!OperatingSystem.IsWindows() && (hasApiToken || hasDiagToken))
                    {
                        _encryptionNotAvailable(_logger, null);
                    }
                }
                catch (Exception ex)
                {
                    _failedToDecryptToken(_logger, ex);
                    // Continue with the original config if decryption fails
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
            _failedToLoad(_logger, _configFilePath, ex);
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
                DiagnosticsBearerToken = OperatingSystem.IsWindows()
                    ? SecureConfigurationHelper.EnsureTokenIsEncrypted(config.DiagnosticsBearerToken)
                    : config.DiagnosticsBearerToken,
                DiscardZeroByteFiles = config.DiscardZeroByteFiles,
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

            _configSaved(_logger, _configFilePath, null);
        }
        catch (Exception ex)
        {
            _failedToSave(_logger, _configFilePath, ex);
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

            _startedWatchingConfig(_logger, _configFilePath, null);
        }
        catch (Exception ex)
        {
            _failedToStartWatcher(_logger, ex);
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

            _configReloadedInfo(_logger, _configFilePath, null);
        }
        catch (Exception ex)
        {
            _failedReloadWarning(_logger, ex);
        }
    }

    private async Task CreateDefaultConfigurationAsync(CancellationToken cancellationToken)
    {

        // Only include properties that are present in the current ExternalConfiguration schema
        var defaultConfig = new ExternalConfiguration
        {
            Folders = new[] { @"C:\temp\watch" },
            ApiEndpoint = "http://localhost:8080/api/files",
            PostFileContents = false,
            MoveProcessedFiles = false,
            ProcessedFolder = "processed",
            AllowedExtensions = [".txt", ".json", ".xml"],
            ExcludePatterns = [],
            IncludeSubdirectories = true,
            DebounceMilliseconds = 1000,
            Retries = 3,
            RetryDelayMilliseconds = 500,
            WatcherMaxRestartAttempts = 3,
            WatcherRestartDelayMilliseconds = 1000,
            DiscardZeroByteFiles = false,
            DiagnosticsUrlPrefix = "http://localhost:5005/",
            ChannelCapacity = 1000,
            MaxParallelSends = 4,
            FileWatcherInternalBufferSize = 64 * 1024,
            WaitForFileReadyMilliseconds = 0,
            MaxContentBytes = 5 * 1024 * 1024,
            StreamingThresholdBytes = 256 * 1024,
            EnableCircuitBreaker = false,
            CircuitBreakerFailureThreshold = 5,
            CircuitBreakerOpenDurationMilliseconds = 30_000,
            Logging = new LoggingOptions
            {
                LogType = LogType.Csv,
                FilePathPattern = "logs/FileWatchRest_{0:yyyyMMdd_HHmmss}",
                LogLevel = "Information",
                RetainedFileCountLimit = 14
            }
        };

        // Log the auto-generated diagnostics token once so operators can find it when the config is first created
        if (!string.IsNullOrWhiteSpace(defaultConfig.DiagnosticsBearerToken))
        {
            _autogenDiagnosticsToken(_logger, defaultConfig.DiagnosticsBearerToken, null);
        }

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
            var config = JsonSerializer.Deserialize(json, MyJsonContext.Default.ExternalConfiguration);
            return config;
        }
        catch (Exception ex)
        {
            // Log to console for early startup diagnostics (before logging infrastructure is ready)
            Console.WriteLine($"LoadFromFile failed for {path}: {ex.Message}");
            return null;
        }
    }
}
