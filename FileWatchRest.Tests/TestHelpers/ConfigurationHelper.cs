namespace FileWatchRest.Tests.TestHelpers;

/// <summary>
/// Helper for creating test configurations and options monitors.
/// Replaces legacy ConfigurationService usage in tests.
/// </summary>
public static class ConfigurationHelper {
    /// <summary>
    /// Creates a simple options monitor for testing with default configuration.
    /// </summary>
    public static SimpleOptionsMonitor<ExternalConfiguration> CreateTestOptionsMonitor(ExternalConfiguration? config = null) => new(config ?? new ExternalConfiguration());

    /// <summary>
    /// Creates a test configuration with common defaults.
    /// </summary>
    public static ExternalConfiguration CreateDefaultTestConfiguration() {
        return new ExternalConfiguration {
            Folders = [],
            ApiEndpoint = "http://localhost/api/files",
            BearerToken = null,
            DebounceMilliseconds = 500,
            Retries = 3,
            RetryDelayMilliseconds = 1000,
            EnableCircuitBreaker = false,
            PostFileContents = false,
            MaxContentBytes = 1024 * 1024, // 1MB
            StreamingThresholdBytes = 10 * 1024, // 10KB
            ExcludePatterns = [],
            Logging = new SimpleFileLoggerOptions {
                LogType = LogType.Csv,
                FilePathPattern = "logs/test_{0:yyyyMMdd}.csv",
                LogLevel = LogLevel.Information
            }
        };
    }

    /// <summary>
    /// Creates a configured options monitor for a specific test scenario.
    /// </summary>
    public static SimpleOptionsMonitor<ExternalConfiguration> CreateConfiguredMonitor(
        string? apiEndpoint = null,
        bool postFileContents = false,
        int? streamingThreshold = null,
        List<ExternalConfiguration.WatchedFolderConfig>? folders = null,
        string[]? excludePatterns = null) {
        ExternalConfiguration config = CreateDefaultTestConfiguration();

        if (apiEndpoint is not null) {
            config.ApiEndpoint = apiEndpoint;
        }

        config.PostFileContents = postFileContents;

        if (streamingThreshold.HasValue) {
            config.StreamingThresholdBytes = streamingThreshold.Value;
        }

        if (folders is not null) {
            config.Folders = folders;
        }

        if (excludePatterns is not null) {
            config.ExcludePatterns = excludePatterns;
        }

        return new SimpleOptionsMonitor<ExternalConfiguration>(config);
    }
}
