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
public partial class ExternalConfigurationOptionsMonitor : IOptionsMonitor<ExternalConfiguration>, IDisposable {
    private readonly ILogger<ExternalConfigurationOptionsMonitor> _logger;
    private readonly string _configPath;
    private readonly Lock _sync = new();
    private readonly List<Action<ExternalConfiguration, string?>> _listeners = [];
    private readonly FileSystemWatcher? _watcher;
    private readonly FileSystemEventHandler? _changedHandler;
    private readonly FileSystemEventHandler? _createdHandler;
    private readonly RenamedEventHandler? _renamedHandler;


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
            _watcher = new FileSystemWatcher(directory) {
                Filter = file,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.Size
            };

            // Create handlers as fields so they can be unsubscribed and disposed later
            _changedHandler = async (sender, args) => {
                // debounce slightly
                await Task.Delay(150).ConfigureAwait(false);
                // Try a few times to load the updated file to account for platform timing
                const int maxAttempts = 6;
                int attempt = 0;
                while (attempt++ < maxAttempts) {
                    try {
                        ExternalConfiguration newCfg = await LoadAndMigrateAsync(CancellationToken.None).ConfigureAwait(false);
                        lock (_sync) { CurrentValue = newCfg; }
                        NotifyListeners(newCfg);
                        break;
                    }
                    catch (IOException) {
                        // File may still be locked by writer; wait a bit and retry
                        await Task.Delay(100).ConfigureAwait(false);
                    }
                    catch (Exception ex) {
                        LoggerDelegates.ErrorHandlingConfigChange(_logger, ex);
                        break;
                    }
                }
            };

            _createdHandler = _changedHandler;
            _renamedHandler = (s, e) => {
                // Translate RenamedEventArgs to FileSystemEventArgs for the common handler
                string dir = Path.GetDirectoryName(e.FullPath) ?? string.Empty;
                var fse = new FileSystemEventArgs(WatcherChangeTypes.Renamed, dir, e.Name);
                _changedHandler?.Invoke(s, fse);
            };

            _watcher.Changed += _changedHandler;
            _watcher.Created += _createdHandler;
            _watcher.Renamed += _renamedHandler;
            _watcher.EnableRaisingEvents = true;
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
                Match m = LogLevelRegex().Match(json);
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
                        list.Add(new ExternalConfiguration.WatchedFolderConfig { FolderPath = it.GetString() ?? string.Empty });
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

            if (doc.RootElement.TryGetProperty("Actions", out JsonElement actionsElem) && actionsElem.ValueKind == JsonValueKind.Array) {
                var alist = new List<ExternalConfiguration.ActionConfig>();
                foreach (JsonElement it in actionsElem.EnumerateArray()) {
                    if (it.ValueKind == JsonValueKind.Object) {
                        try {
                            ExternalConfiguration.ActionConfig? ac = it.Deserialize(MyJsonContext.Default.ActionConfig);
                            if (ac is not null) alist.Add(ac);
                        }
                        catch { /* ignore */ }
                    }
                }

                manual.Actions = alist;
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
            cfg.Actions ??= [];
            cfg.Logging ??= new SimpleFileLoggerOptions { LogLevel = LogLevel.Information };
        }

        if (cfg is null) {
            LoggerDelegates.ConfigDeserializationFailed(_logger, _configPath, null);
            return new ExternalConfiguration { ApiEndpoint = "http://localhost:8080/api/files" };
        }

        Configuration.ValidationResult validation = ExternalConfigurationValidator.Validate(cfg);
        if (!validation.IsValid) {
            // Emit one log entry per validation failure (structured fields) so CSV logs have discrete columns
            foreach (ValidationFailure vf in validation.Errors) {
                LoggerDelegates.ConfigValidationFailure(_logger, vf.PropertyName, vf.ErrorMessage, null);
            }
            // also emit the legacy aggregated message for backward compatibility
            string errors = string.Join("; ", validation.Errors.Select(e => $"{e.PropertyName}: {e.ErrorMessage}"));
            LoggerDelegates.LoadedConfigurationInvalid(_logger, errors, null);
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
                            fb.Folders.Add(new ExternalConfiguration.WatchedFolderConfig { FolderPath = it.GetString() ?? string.Empty });
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

                // Per-action tokens: decrypt if needed for runtime
                if (cfg.Actions is not null) {
                    foreach (ExternalConfiguration.ActionConfig a in cfg.Actions) {
                        if (!string.IsNullOrWhiteSpace(a.BearerToken) && SecureConfigurationHelper.IsTokenEncrypted(a.BearerToken)) {
                            a.BearerToken = SecureConfigurationHelper.DecryptBearerToken(a.BearerToken!);
                        }
                    }
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
                    // Ensure action tokens are encrypted in the saved copy
                    if (cfg.Actions is not null) {
                        // Prefer to preserve the original Actions JSON as-is, only modifying tokens to be encrypted.
                        // If we parsed a JsonDocument of the source, use it as the baseline so we don't add
                        // properties that weren't present in the original file.
                        if (doc is not null) {
                            var root = JsonNode.Parse(json);
                            if (root is JsonObject rootObj) {
                                // Use cfg as the source-of-truth for token updates so we don't change other fields.
                                if (!string.IsNullOrWhiteSpace(cfg.BearerToken) && rootObj.TryGetPropertyValue("BearerToken", out _)) {
                                    rootObj["BearerToken"] = SecureConfigurationHelper.EnsureTokenIsEncrypted(cfg.BearerToken);
                                }

                                if (!string.IsNullOrWhiteSpace(cfg.DiagnosticsBearerToken) && rootObj.TryGetPropertyValue("DiagnosticsBearerToken", out _)) {
                                    rootObj["DiagnosticsBearerToken"] = SecureConfigurationHelper.EnsureTokenIsEncrypted(cfg.DiagnosticsBearerToken);
                                }

                                // Update per-action tokens by matching action Name; leave actions untouched otherwise
                                if (cfg.Actions is not null && rootObj.TryGetPropertyValue("Actions", out JsonNode? actionsNode) && actionsNode is JsonArray actionsArr) {
                                    // Build lookup of action-name -> encrypted token for actions that actually have tokens
                                    var actionTokenMap = cfg.Actions
                                        .Where(a => !string.IsNullOrWhiteSpace(a.Name) && !string.IsNullOrWhiteSpace(a.BearerToken))
                                        .ToDictionary(a => a.Name!, a => SecureConfigurationHelper.EnsureTokenIsEncrypted(a.BearerToken!));

                                    if (actionTokenMap.Count > 0) {
                                        foreach (JsonNode? actionNode in actionsArr) {
                                            if (actionNode is JsonObject actionObj && actionObj.TryGetPropertyValue("Name", out JsonNode? nameNode) && nameNode is JsonValue) {
                                                string? nameVal = nameNode.GetValue<string?>();
                                                if (!string.IsNullOrWhiteSpace(nameVal) && actionTokenMap.TryGetValue(nameVal, out string? encrypted)) {
                                                    actionObj["BearerToken"] = encrypted;
                                                }
                                            }
                                        }
                                    }
                                }

                                string outJson = root.ToJsonString(MyJsonContext.SaveOptions);
                                await File.WriteAllTextAsync(_configPath, outJson, ct);
                                LoggerDelegates.ConfigSaved(_logger, _configPath, null);
                            }
                            else {
                                // fallback: couldn't get a mutable JsonObject for the original
                                // document. Write a minimal sensible actions copy to avoid
                                // introducing unrelated or default-valued fields.
                                copy.Actions = [.. cfg.Actions.Select(a => new ExternalConfiguration.ActionConfig {
                                    Name = a.Name,
                                    ActionType = a.ActionType,
                                    Arguments = a.Arguments,
                                    BearerToken = string.IsNullOrWhiteSpace(a.BearerToken) ? a.BearerToken : SecureConfigurationHelper.EnsureTokenIsEncrypted(a.BearerToken)
                                })];
                                await SaveConfigAsync(copy, ct);
                            }
                        }
                        else {
                            // Fallback: minimal sensible actions copy (avoid mixing unrelated fields)
                            copy.Actions = [.. cfg.Actions.Select(a => new ExternalConfiguration.ActionConfig {
                                Name = a.Name,
                                ActionType = a.ActionType,
                                Arguments = a.Arguments,
                                BearerToken = string.IsNullOrWhiteSpace(a.BearerToken) ? a.BearerToken : SecureConfigurationHelper.EnsureTokenIsEncrypted(a.BearerToken)
                            })];
                            await SaveConfigAsync(copy, ct);
                        }
                    }
                    else {
                        await SaveConfigAsync(copy, ct);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is not IOException and not UnauthorizedAccessException and not System.Security.SecurityException) {
            // Only treat non-IO/security exceptions as decryption failures. IO/security errors
            // during Save are already logged by SaveConfigAsync; avoid mislabeling them as
            // decryption problems which confuses users (see many "Failed to decrypt bearer token" logs).
            LoggerDelegates.FailedToDecryptToken(_logger, ex);
        }

        return cfg;
    }

    private async Task SaveConfigAsync(ExternalConfiguration cfg, CancellationToken ct) {
        try {
            // Use the source-generated context for AOT/native AOT compatibility
            string json = JsonSerializer.Serialize(cfg, MyJsonContext.Default.ExternalConfiguration);
            await AtomicWriteTextAsync(_configPath, json, ct);
            LoggerDelegates.ConfigSaved(_logger, _configPath, null);
        }
        catch (Exception ex) {
            LoggerDelegates.FailedToSave(_logger, _configPath, ex);
            throw;
        }
    }

    private static async Task AtomicWriteTextAsync(string path, string content, CancellationToken ct) {
        // Write to a temp file on the same directory then replace the target atomically when possible.
        string dir = Path.GetDirectoryName(path) ?? string.Empty;
        string tempFile = Path.Combine(dir, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

        // Ensure directory exists
        Directory.CreateDirectory(dir);

        // Write the content to a temp file first
        await File.WriteAllTextAsync(tempFile, content, ct);

        try {
            if (File.Exists(path)) {
                try {
                    // Prefer File.Replace for an atomic swap when possible
                    File.Replace(tempFile, path, null);
                }
                catch (IOException) {
                    // File.Replace can fail if the destination is locked; try a force-overwrite move
                    // Note: this may still fail if another process holds an exclusive handle.
                    File.Move(tempFile, path, true);
                }
            }
            else {
                // Move if destination doesn't exist
                File.Move(tempFile, path);
            }
        }
        catch (PlatformNotSupportedException) {
            // Fall back to overwrite move
            File.Move(tempFile, path, true);
        }
        catch (Exception) {
            // On any failure, attempt to clean up the temp file and rethrow so caller logs appropriately
            try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
            throw;
        }
    }

    private static ExternalConfiguration CreateDefault() {
        // Minimal, sensible default: one watched folder and a matching action.
        // Keep this small to avoid persisting lots of implicit defaults.
        return new ExternalConfiguration {
            Folders = [new() {
                FolderPath = @"C:\temp\watch",
                ActionName = "Default"
            }],
            DiagnosticsUrlPrefix = "http://localhost:5005/",
            DiagnosticsBearerToken = null,
            Actions = [new ExternalConfiguration.ActionConfig {
                Name = "Default",
                ActionType = ExternalConfiguration.FolderActionType.RestPost,
                ApiEndpoint = "http://localhost:8080/api/files",
                BearerToken = null,
            }],
            Logging = new SimpleFileLoggerOptions {
                LogType = LogType.Csv,
                FilePathPattern = "logs/FileWatchRest_{0:yyyyMMdd_HHmmss}",
                LogLevel = LogLevel.Information
            }
        };
    }

    [GeneratedRegex("\"LogLevel\"\\s*:\\s*\"(?<lvl>[A-Za-z]+)\"")]
    private static partial Regex LogLevelRegex();

    public void Dispose() {
        try {
            if (_watcher is not null) {
                try {
                    if (_changedHandler is not null) _watcher.Changed -= _changedHandler;
                    if (_createdHandler is not null) _watcher.Created -= _createdHandler;
                    if (_renamedHandler is not null) _watcher.Renamed -= _renamedHandler;
                }
                catch { /* best-effort */ }

                try { _watcher.EnableRaisingEvents = false; } catch { }
                try { _watcher.Dispose(); } catch { }
            }
        }
        finally {
            GC.SuppressFinalize(this);
        }
    }
}
