namespace FileWatchRest.Services;

/// <summary>
/// Implements IOptionsMonitor pattern for external JSON configuration.
///
/// This allows the service to:
/// - Use standard dependency injection for configuration
/// - Integrate with .NET's options pattern (IOptionsMonitor)
/// - Support custom config location (ProgramData) with file watching
/// - Maintain security features (token encryption) outside standard IConfiguration
///
/// The adapter loads configuration synchronously during DI container construction
/// and propagates file-change notifications to IOptionsMonitor subscribers.
/// </summary>
public partial class ExternalConfigurationOptionsMonitor : IOptionsMonitor<ExternalConfiguration> {
    private readonly ILogger<ExternalConfigurationOptionsMonitor> _logger;
    private readonly string _configPath;
    private readonly Lock _sync = new();
    private readonly List<Action<ExternalConfiguration, string?>> _listeners = [];

    /// <summary>
    /// Test-friendly constructor: accepts explicit config path
    /// </summary>
    /// <param name="configFilePath"></param>
    /// <param name="logger"></param>
    /// <param name="loggerFactory"></param>
    public ExternalConfigurationOptionsMonitor(string configFilePath, ILogger<ExternalConfigurationOptionsMonitor> logger, ILoggerFactory loggerFactory) {
        _logger = logger;
        _configPath = configFilePath ?? throw new ArgumentNullException(nameof(configFilePath));

        // Load initial configuration
        try {
            CurrentValue = LoadAndMigrateAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception ex) {
            LoggerDelegates.FailedToLoadInitial(_logger, ex);
            CurrentValue = new ExternalConfiguration();
        }

        // Watch for changes using FileSystemWatcher and reload
        try {
            string directory = Path.GetDirectoryName(_configPath) ?? string.Empty;
            string file = Path.GetFileName(_configPath);
            var watcher = new FileSystemWatcher(directory) {
                Filter = file,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.Size
            };

            async void handle(object sender, FileSystemEventArgs args) {
                // debounce slightly
                await Task.Delay(150);
                try {
                    ExternalConfiguration newCfg = await LoadAndMigrateAsync(CancellationToken.None);
                    lock (_sync) { CurrentValue = newCfg; }
                    NotifyListeners(newCfg);
                }
                catch (Exception ex) {
                    LoggerDelegates.ErrorHandlingConfigChange(_logger, ex);
                }
            }

            watcher.Changed += handle;
            watcher.Created += handle;
            watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) {
            LoggerDelegates.FailedToStartWatcherConfig(_logger, ex);
        }
    }

    /// <summary>
    /// DI-friendly constructor: compute the default ProgramData path and delegate to main ctor
    /// </summary>
    /// <param name="logger"></param>
    /// <param name="loggerFactory"></param>
    public ExternalConfigurationOptionsMonitor(ILogger<ExternalConfigurationOptionsMonitor> logger, ILoggerFactory loggerFactory)
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "FileWatchRest", "FileWatchRest.json"), logger, loggerFactory) {
    }

    private void NotifyListeners(ExternalConfiguration newConfig) {
        Action<ExternalConfiguration, string?>[] copy;
        lock (_sync) { copy = [.. _listeners]; }
        foreach (Action<ExternalConfiguration, string?> cb in copy) {
            try { cb(newConfig, null); }
            catch (Exception ex) { LoggerDelegates.ListenerThrew(_logger, ex); }
        }
    }

    public ExternalConfiguration CurrentValue { get; private set; } = new();

    public ExternalConfiguration Get(string? name) => CurrentValue;

    public IDisposable OnChange(Action<ExternalConfiguration, string?> listener) {
        lock (_sync) { _listeners.Add(listener); }
        return new DisposableAction(() => { lock (_sync) { _listeners.Remove(listener); } });
    }

    private sealed class DisposableAction(Action action) : IDisposable {
        private readonly Action _action = action; private int _disposed;

        public void Dispose() {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) {
                _action();
            }
        }
    }

    private async Task<ExternalConfiguration> LoadAndMigrateAsync(CancellationToken ct) {
        if (!File.Exists(_configPath)) {
            LoggerDelegates.ConfigFileNotFound(_logger, _configPath, null);
            ExternalConfiguration def = CreateDefault();
            await SaveConfigAsync(def, ct);
            return def;
        }

        string json = await File.ReadAllTextAsync(_configPath, ct);
        JsonDocument? doc = null;
        try { doc = JsonDocument.Parse(json); } catch { doc = null; }

        // capture raw values
        string? apiFromDoc = null;
        LogLevel? topLevelParsed = null;
        if (doc is not null) {
            if (doc.RootElement.TryGetProperty("ApiEndpoint", out JsonElement ae) && ae.ValueKind == JsonValueKind.String) {
                apiFromDoc = ae.GetString();
            }

            if (doc.RootElement.TryGetProperty("LogLevel", out JsonElement tl) && tl.ValueKind == JsonValueKind.String) {
                Enum.TryParse(tl.GetString(), true, out LogLevel parsedTop);
                topLevelParsed = parsedTop;
            }
            else {
                Match m = MyRegex().Match(json);
                if (m.Success && Enum.TryParse(m.Groups["lvl"].Value, true, out LogLevel parsed)) {
                    topLevelParsed = parsed;
                }
            }
        }

        ExternalConfiguration? cfg = null;
        try {
            cfg = JsonSerializer.Deserialize(json, MyJsonContext.Default.ExternalConfiguration);
        }
        catch { cfg = null; }

        if (cfg is null && doc is not null) {
            var manual = new ExternalConfiguration();
            if (doc.RootElement.TryGetProperty("ApiEndpoint", out JsonElement apiE) && apiE.ValueKind == JsonValueKind.String) {
                manual.ApiEndpoint = apiE.GetString();
            }

            if (doc.RootElement.TryGetProperty("ProcessedFolder", out JsonElement pf) && pf.ValueKind == JsonValueKind.String) {
                manual.ProcessedFolder = pf.GetString()!;
            }

            if (doc.RootElement.TryGetProperty("Folders", out JsonElement foldersElem) && foldersElem.ValueKind == JsonValueKind.Array) {
                var list = new List<ExternalConfiguration.WatchedFolderConfig>();
                foreach (JsonElement it in foldersElem.EnumerateArray()) {
                    if (it.ValueKind == JsonValueKind.String) {
                        list.Add(new ExternalConfiguration.WatchedFolderConfig { FolderPath = it.GetString() ?? string.Empty, ActionType = ExternalConfiguration.FolderActionType.RestPost });
                    }
                    else if (it.ValueKind == JsonValueKind.Object) {
                        ExternalConfiguration.WatchedFolderConfig? folderConfig = it.Deserialize(MyJsonContext.Default.WatchedFolderConfig);
                        if (folderConfig is not null) {
                            list.Add(folderConfig);
                        }
                    }
                }
                manual.Folders = list;
            }

            if (doc.RootElement.TryGetProperty("Logging", out JsonElement loggingElem) && loggingElem.ValueKind == JsonValueKind.Object) {
                var lo = new SimpleFileLoggerOptions { LogLevel = LogLevel.Information };
                if (loggingElem.TryGetProperty("LogLevel", out JsonElement ll)) {
                    if (ll.ValueKind == JsonValueKind.String && Enum.TryParse(ll.GetString(), true, out LogLevel lv)) {
                        lo.LogLevel = lv;
                    }
                    else if (ll.ValueKind == JsonValueKind.Null) {
                        lo.LogLevel = null;
                    }
                }
                manual.Logging = lo;
            }

            manual.Logging ??= new SimpleFileLoggerOptions { LogLevel = topLevelParsed ?? LogLevel.Information };
            cfg = manual;
        }

        if (cfg is not null) {
            if (string.IsNullOrWhiteSpace(cfg.ApiEndpoint) && !string.IsNullOrWhiteSpace(apiFromDoc)) {
                cfg.ApiEndpoint = apiFromDoc;
            }

            if (topLevelParsed.HasValue) { cfg.Logging ??= new SimpleFileLoggerOptions(); cfg.Logging.LogLevel = topLevelParsed.Value; }

            // Migrate legacy Logging.MinimumLevel -> Logging.LogLevel when present in the raw document
            LogLevel? loggingMinimumParsed = null;
            if (doc is not null &&
                doc.RootElement.TryGetProperty("Logging", out JsonElement loggingElem) &&
                loggingElem.ValueKind == JsonValueKind.Object &&
                loggingElem.TryGetProperty("MinimumLevel", out JsonElement min) &&
                min.ValueKind == JsonValueKind.String &&
                Enum.TryParse(min.GetString(), true, out LogLevel parsedMin)
            ) {
                loggingMinimumParsed = parsedMin;
            }

            bool migrated = false;
            var auditLines = new List<string>();
            if (topLevelParsed.HasValue) {
                migrated = true;
                auditLines.Add("TopLevel LogLevel -> Logging.LogLevel");
            }
            else if (loggingMinimumParsed.HasValue) {
                cfg.Logging ??= new SimpleFileLoggerOptions();
                cfg.Logging.LogLevel = loggingMinimumParsed.Value;
                migrated = true;
                auditLines.Add("Logging.MinimumLevel -> Logging.LogLevel");
            }

            if (migrated) {
                try {
                    string auditPath = Path.Combine(Path.GetDirectoryName(_configPath) ?? string.Empty, "migration-audit.log");
                    await File.AppendAllLinesAsync(auditPath, auditLines.Select(l => $"{DateTime.UtcNow:O} {l}"), ct);
                    LoggerDelegates.MigratedLoggingSettings(_logger, _configPath, null);
                    await SaveConfigAsync(cfg, ct);
                }
                catch { /* best-effort */ }
            }
            cfg.ExcludePatterns ??= [];
            cfg.AllowedExtensions ??= [];
            cfg.Folders ??= [];
            cfg.Logging ??= new SimpleFileLoggerOptions { LogLevel = LogLevel.Information };
        }

        if (cfg is null) {
            LoggerDelegates.ConfigDeserializationFailed(_logger, _configPath, null);
            return new ExternalConfiguration { ApiEndpoint = "http://localhost:8080/api/files" };
        }

        Configuration.ValidationResult validation = ExternalConfigurationValidator.Validate(cfg);
        if (!validation.IsValid) {
            LoggerDelegates.LoadedConfigurationInvalid(_logger, string.Join("; ", validation.Errors.Select(e => e.ErrorMessage)), null);
            // try minimal fallback
            if (doc is not null) {
                var fb = new ExternalConfiguration();
                if (doc.RootElement.TryGetProperty("ApiEndpoint", out JsonElement ae2) && ae2.ValueKind == JsonValueKind.String) {
                    fb.ApiEndpoint = ae2.GetString();
                }

                fb.Folders = [];
                if (doc.RootElement.TryGetProperty("Folders", out JsonElement fe) && fe.ValueKind == JsonValueKind.Array) {
                    foreach (JsonElement it in fe.EnumerateArray()) {
                        if (it.ValueKind == JsonValueKind.String) {
                            fb.Folders.Add(new ExternalConfiguration.WatchedFolderConfig { FolderPath = it.GetString() ?? string.Empty, ActionType = ExternalConfiguration.FolderActionType.RestPost });
                        }
                        else if (it.ValueKind == JsonValueKind.Object) {
                            ExternalConfiguration.WatchedFolderConfig? fc = it.Deserialize(MyJsonContext.Default.WatchedFolderConfig);
                            if (fc is not null) {
                                fb.Folders.Add(fc);
                            }
                        }
                    }
                }
                Configuration.ValidationResult fbv = ExternalConfigurationValidator.Validate(fb);
                if (fbv.IsValid) {
                    return fb;
                }
            }

            return new ExternalConfiguration();
        }

        // Token handling: decrypt for runtime use; if plain and running on Windows, encrypt and save a persisted copy
        try {
            bool hasApi = !string.IsNullOrWhiteSpace(cfg.BearerToken);
            bool hasDiag = !string.IsNullOrWhiteSpace(cfg.DiagnosticsBearerToken);
            if (OperatingSystem.IsWindows()) {
                if (hasApi && SecureConfigurationHelper.IsTokenEncrypted(cfg.BearerToken)) {
                    cfg.BearerToken = SecureConfigurationHelper.DecryptBearerToken(cfg.BearerToken!);
                }

                if (hasDiag && SecureConfigurationHelper.IsTokenEncrypted(cfg.DiagnosticsBearerToken)) {
                    cfg.DiagnosticsBearerToken = SecureConfigurationHelper.DecryptBearerToken(cfg.DiagnosticsBearerToken!);
                }

                if ((hasApi && !SecureConfigurationHelper.IsTokenEncrypted(cfg.BearerToken)) || (hasDiag && !SecureConfigurationHelper.IsTokenEncrypted(cfg.DiagnosticsBearerToken))) {
                    // Save encrypted copy
                    var copy = new ExternalConfiguration {
                        Folders = cfg.Folders,
                        ApiEndpoint = cfg.ApiEndpoint,
                        BearerToken = hasApi ? SecureConfigurationHelper.EnsureTokenIsEncrypted(cfg.BearerToken) : cfg.BearerToken,
                        DiagnosticsBearerToken = hasDiag ? SecureConfigurationHelper.EnsureTokenIsEncrypted(cfg.DiagnosticsBearerToken) : cfg.DiagnosticsBearerToken,
                        PostFileContents = cfg.PostFileContents,
                        ProcessedFolder = cfg.ProcessedFolder,
                        MoveProcessedFiles = cfg.MoveProcessedFiles,
                        AllowedExtensions = cfg.AllowedExtensions,
                        ExcludePatterns = cfg.ExcludePatterns,
                        IncludeSubdirectories = cfg.IncludeSubdirectories,
                        DebounceMilliseconds = cfg.DebounceMilliseconds,
                        Retries = cfg.Retries,
                        RetryDelayMilliseconds = cfg.RetryDelayMilliseconds,
                        WatcherMaxRestartAttempts = cfg.WatcherMaxRestartAttempts,
                        WatcherRestartDelayMilliseconds = cfg.WatcherRestartDelayMilliseconds,
                        DiagnosticsUrlPrefix = cfg.DiagnosticsUrlPrefix,
                        ChannelCapacity = cfg.ChannelCapacity,
                        MaxParallelSends = cfg.MaxParallelSends,
                        FileWatcherInternalBufferSize = cfg.FileWatcherInternalBufferSize,
                        WaitForFileReadyMilliseconds = cfg.WaitForFileReadyMilliseconds,
                        MaxContentBytes = cfg.MaxContentBytes,
                        StreamingThresholdBytes = cfg.StreamingThresholdBytes,
                        EnableCircuitBreaker = cfg.EnableCircuitBreaker,
                        CircuitBreakerFailureThreshold = cfg.CircuitBreakerFailureThreshold,
                        CircuitBreakerOpenDurationMilliseconds = cfg.CircuitBreakerOpenDurationMilliseconds,
                        Logging = cfg.Logging
                    };
                    await SaveConfigAsync(copy, ct);
                }
            }
        }
        catch (Exception ex) {
            LoggerDelegates.FailedToDecryptToken(_logger, ex);
        }

        return cfg;
    }

    private async Task SaveConfigAsync(ExternalConfiguration cfg) => await SaveConfigAsync(cfg, CancellationToken.None);

    private async Task SaveConfigAsync(ExternalConfiguration cfg, CancellationToken ct) {
        try {
            string json = JsonSerializer.Serialize(cfg, MyJsonContext.Default.ExternalConfiguration);
            await File.WriteAllTextAsync(_configPath, json, ct);
            LoggerDelegates.ConfigSaved(_logger, _configPath, null);
        }
        catch (Exception ex) {
            LoggerDelegates.FailedToSave(_logger, _configPath, ex);
            throw;
        }
    }

    private static ExternalConfiguration CreateDefault() {
        return new ExternalConfiguration {
            Folders = [new() { FolderPath = @"C:\temp\watch" }],
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
            Logging = new SimpleFileLoggerOptions {
                LogType = LogType.Csv,
                FilePathPattern = "logs/FileWatchRest_{0:yyyyMMdd_HHmmss}",
                LogLevel = LogLevel.Information
            }
        };
    }

    [GeneratedRegex("\"LogLevel\"\\s*:\\s*\"(?<lvl>[A-Za-z]+)\"")]
    private static partial Regex MyRegex();
}
