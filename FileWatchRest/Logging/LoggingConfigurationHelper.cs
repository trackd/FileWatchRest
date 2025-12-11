namespace FileWatchRest.Logging;

/// <summary>
/// Encapsulates logging configuration initialization logic, including:
/// - Resolving log file paths from configuration
/// - Creating CSV headers
/// - Building SimpleFileLoggerOptions from ExternalConfiguration
/// </summary>
public static class LoggingConfigurationHelper {
    /// <summary>
    /// Load logging options from configuration file, or use defaults if file doesn't exist.
    /// </summary>
    public static SimpleFileLoggerOptions LoadLoggingOptions(string configPath) {
        if (File.Exists(configPath)) {
            try {
                string json = File.ReadAllText(configPath);
                ExternalConfiguration? extConfig = JsonSerializer.Deserialize(json, MyJsonContext.Default.ExternalConfiguration);
                if (extConfig?.Logging is not null) {
                    return extConfig.Logging;
                }
            }
            catch {
                // Ignore - will use defaults below
            }
        }

        return CreateDefaultLoggingOptions();
    }

    /// <summary>
    /// Create default logging options when none are configured.
    /// </summary>
    public static SimpleFileLoggerOptions CreateDefaultLoggingOptions() {
        return new SimpleFileLoggerOptions {
            LogType = LogType.Csv,
            FilePathPattern = "logs/FileWatchRest_{0:yyyyMMdd_HHmmss}",
            LogLevel = LogLevel.Information,
            RetainedDays = 14
        };
    }

    /// <summary>
    /// Resolve a file path pattern to an absolute path, treating relative paths as relative to baseDir.
    /// </summary>
    public static string ResolvePath(string pattern, string baseDir) {
        return string.IsNullOrWhiteSpace(pattern)
            ? Path.Combine(baseDir, "logs", "FileWatchRest.log")
            : Path.IsPathRooted(pattern) ? pattern : Path.Combine(baseDir, pattern.Replace('/', Path.DirectorySeparatorChar));
    }

    /// <summary>
    /// Format a log file path with timestamp, then add appropriate extension (.csv or .json).
    /// </summary>
    public static string FormatLogFilePath(string basePattern, LogType logType) {
        string formatted = string.Format(CultureInfo.InvariantCulture, basePattern, DateTime.Now);
        string extension = logType switch {
            LogType.Csv => ".csv",
            LogType.Json => ".json",
            LogType.Both => ".csv", // CSV is primary for Both
            _ => ".log"
        };

        return Path.HasExtension(formatted) ? formatted : formatted + extension;
    }

    /// <summary>
    /// Ensure CSV file exists with proper header. No-op if file already exists or if CSV logging is not enabled.
    /// </summary>
    public static void EnsureCsvHeader(string csvLogFilePath, LogType logType) {
        if (logType is not (LogType.Csv or LogType.Both)) {
            return;
        }

        try {
            if (!File.Exists(csvLogFilePath)) {
                File.WriteAllText(csvLogFilePath, "Timestamp,Level,Message,Category,Exception,StatusCode\n");
            }
        }
        catch {
            // Best-effort: if header creation fails, logging will still work
        }
    }

    /// <summary>
    /// Build SimpleFileLoggerOptions suitable for the SimpleFileLoggerProvider.
    /// Resolves relative FilePathPattern using configDir as base.
    /// </summary>
    public static SimpleFileLoggerOptions BuildFileLoggerOptions(SimpleFileLoggerOptions loggingOptions, string configDir) {
        string resolvedPattern = loggingOptions.FilePathPattern ?? "logs/FileWatchRest_{0:yyyyMMdd_HHmmss}";
        if (!Path.IsPathRooted(resolvedPattern)) {
            resolvedPattern = Path.Combine(configDir, resolvedPattern.Replace('/', Path.DirectorySeparatorChar));
        }

        return new SimpleFileLoggerOptions {
            LogType = loggingOptions.LogType,
            FilePathPattern = resolvedPattern,
            LogLevel = loggingOptions.LogLevel,
            RetainedDays = loggingOptions.RetainedDays
        };
    }

    /// <summary>
    /// Configure logging on the host builder with file logger setup.
    /// Loads logging options, ensures log directories and CSV headers, and registers the custom provider.
    /// </summary>
    public static IHostBuilder ConfigureFileLogging(this IHostBuilder builder, string configPath, string baseDir) {
        // Load logging configuration
        SimpleFileLoggerOptions loggingOptions = LoadLoggingOptions(configPath);
        SimpleFileLoggerOptions fileLoggerOptions = BuildFileLoggerOptions(loggingOptions, baseDir);

        // Ensure log directory and CSV header
        Directory.CreateDirectory(baseDir);
        string csvLogPath = FormatLogFilePath(fileLoggerOptions.FilePathPattern, LogType.Csv);
        EnsureCsvHeader(csvLogPath, loggingOptions.LogType);

        return builder.ConfigureLogging((context, logging) => {
            logging.ClearProviders();
            logging.SetMinimumLevel(LogLevel.Trace);
            logging.AddProvider(new SimpleFileLoggerProvider(fileLoggerOptions));
        });
    }

    /// <summary>
    /// Log startup configuration details including version and config structure.
    /// </summary>
    public static void LogStartupConfiguration(ILogger logger, ExternalConfiguration config, string configPath) {
        string version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown";
        LoggerDelegates.StartupVersion(logger, version, null);
        LoggerDelegates.StartupConfigPath(logger, configPath, null);

        // Log global settings
        if (config.AllowedExtensions?.Length > 0) {
            LoggerDelegates.StartupConfigValue(logger, "Global AllowedExtensions", string.Join(", ", config.AllowedExtensions), null);
        }

        if (config.ExcludePatterns?.Length > 0) {
            LoggerDelegates.StartupConfigValue(logger, "Global ExcludePatterns", string.Join(", ", config.ExcludePatterns), null);
        }

        // Log folders and their actions
        if (config.Folders?.Count > 0) {
            LoggerDelegates.StartupConfigValue(logger, "Folders Count", config.Folders.Count.ToString(CultureInfo.InvariantCulture), null);
            foreach (ExternalConfiguration.WatchedFolderConfig folder in config.Folders) {
                LoggerDelegates.StartupConfigValue(logger, "Folder", folder.FolderPath ?? "(null)", null);
                if (!string.IsNullOrEmpty(folder.ActionName)) {
                    LoggerDelegates.StartupConfigValue(logger, "  Action", folder.ActionName, null);
                }
            }
        }

        // Log actions
        if (config.Actions?.Count > 0) {
            LoggerDelegates.StartupConfigValue(logger, "Actions Count", config.Actions.Count.ToString(CultureInfo.InvariantCulture), null);
            foreach (ExternalConfiguration.ActionConfig action in config.Actions) {
                string actionName = string.IsNullOrEmpty(action.Name) ? "(unnamed)" : action.Name;
                LoggerDelegates.StartupConfigValue(logger, "Action", actionName, null);
                LoggerDelegates.StartupConfigValue(logger, "  Type", action.ActionType.ToString(), null);

                if (!string.IsNullOrEmpty(action.ApiEndpoint)) {
                    LoggerDelegates.StartupConfigValue(logger, "  ApiEndpoint", action.ApiEndpoint, null);
                }

                if (!string.IsNullOrEmpty(action.ScriptPath)) {
                    LoggerDelegates.StartupConfigValue(logger, "  ScriptPath", action.ScriptPath, null);
                }

                if (!string.IsNullOrEmpty(action.ExecutablePath)) {
                    LoggerDelegates.StartupConfigValue(logger, "  ExecutablePath", action.ExecutablePath, null);
                }

                if (action.AllowedExtensions is { Length: > 0 }) {
                    string extList = string.Join(", ", action.AllowedExtensions);
                    LoggerDelegates.StartupConfigValue(logger, "  AllowedExtensions", extList, null);
                }

                if (action.ExcludePatterns is { Length: > 0 }) {
                    string patternList = string.Join(", ", action.ExcludePatterns);
                    LoggerDelegates.StartupConfigValue(logger, "  ExcludePatterns", patternList, null);
                }
            }
        }

        LoggerDelegates.StartupComplete(logger, null);
    }
}
