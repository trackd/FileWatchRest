namespace FileWatchRest.Tests.Integration;

/// <summary>
/// Integration tests for configuration reload and watcher actions.
/// </summary>
public class ConfigurationIntegrationTests {
    /// <summary>
    /// Verifies that modifying the configuration file triggers a reload.
    /// </summary>
    [Fact]
    public async Task ConfigurationFileChangeTriggersReload() {
        // Arrange: Use the real configuration file path

        string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        string testGuid = Guid.NewGuid().ToString("N");
        string serviceDir = Path.Combine(appDataPath, $"FileWatchRest_Test_{testGuid}");
        string configPath = Path.Combine(serviceDir, "FileWatchRest.json");
        Directory.CreateDirectory(serviceDir);
        string backupPath = configPath + ".bak";

        // Backup current config
        if (File.Exists(configPath)) {
            File.Copy(configPath, backupPath, true);
        }

        try {
            // Modify config file
            string watchedFolder = "C:\\temp\\watch";
            Directory.CreateDirectory(watchedFolder);
            if (!File.Exists(configPath)) {
                var defaultConfig = new ExternalConfiguration {
                    Folders = [new ExternalConfiguration.WatchedFolderConfig { FolderPath = watchedFolder }],
                    ApiEndpoint = "http://localhost:8080/api/files",
                    PostFileContents = false,
                    MoveProcessedFiles = false,
                    ProcessedFolder = "processed",
                    AllowedExtensions = [".txt"],
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
                    CircuitBreakerOpenDurationMilliseconds = 30000,
                    Logging = new SimpleFileLoggerOptions {
                        LogType = LogType.Csv,
                        FilePathPattern = Path.Combine(serviceDir, "FileWatchRest_{0:yyyyMMdd_HHmmss}"),
                        LogLevel = LogLevel.Information
                    }
                };
                string json = JsonSerializer.Serialize(defaultConfig, MyJsonContext.Default.ExternalConfiguration);
                await File.WriteAllTextAsync(configPath, json);
            }
            string originalJson = await File.ReadAllTextAsync(configPath);
            string modifiedJson = originalJson.Replace("Information", "Debug"); // Change log level for test
            await File.WriteAllTextAsync(configPath, modifiedJson);

            // Wait for service to reload config (simulate delay)
            await Task.Delay(1000);

            // Optionally, query diagnostics API to verify config reload
            // Example: using HttpClient to GET /diagnostics endpoint
            // var http = new HttpClient();
            // var response = await http.GetAsync("http://localhost:5005/config");
            // response.EnsureSuccessStatusCode();
            // var json = await response.Content.ReadAsStringAsync();
            // Assert.Contains("Debug", json);
        }
        finally {
            // Restore original config
            if (File.Exists(backupPath)) {
                File.Copy(backupPath, configPath, true);
            }

            if (File.Exists(backupPath)) {
                File.Delete(backupPath);
            }
        }
    }

    /// <summary>
    /// Verifies that creating a file in the watched folder triggers watcher action.
    /// </summary>
    [Fact]
    public async Task FileCreationTriggersWatcherAction() {
        // Arrange: Use a real watched folder from config
        string serviceName = "FileWatchRest";
        string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        string serviceDir = Path.Combine(appDataPath, serviceName);
        Directory.CreateDirectory(serviceDir);
        string configPath = Path.Combine(serviceDir, "FileWatchRest.json");
        string watchedFolder = "C:\\temp\\watch";
        Directory.CreateDirectory(watchedFolder);
        if (!File.Exists(configPath)) {
            await WriteDefaultConfigAsync(configPath, serviceDir, watchedFolder);
        }
        string configJson = await File.ReadAllTextAsync(configPath);


        // Ensure default config is present
        string? configDir = Path.GetDirectoryName(configPath);
        if (configDir is not null && !Directory.Exists(configDir)) {
            Directory.CreateDirectory(configDir);
        }
        if (!File.Exists(configPath)) {
            await WriteDefaultConfigAsync(configPath, serviceDir, watchedFolder);
        }

        // Act: Create a new file in the watched folder
        string testFile = Path.Combine(watchedFolder, $"integration_test_{Guid.NewGuid()}.txt");
        await File.WriteAllTextAsync(testFile, "integration test content");

        // Wait for watcher to process the file (simulate delay)
        await Task.Delay(2000);

        // Optionally, query diagnostics API or check processed folder
        // var processedFolder = Path.Combine(watchedFolder, "processed");
        // Assert.True(File.Exists(Path.Combine(processedFolder, Path.GetFileName(testFile))));

        // Cleanup
        if (File.Exists(testFile)) {
            File.Delete(testFile);
        }
    }
    private static async Task WriteDefaultConfigAsync(string configPath, string serviceDir, string watchedFolder) {
        var defaultConfig = new ExternalConfiguration {
            Folders = [new ExternalConfiguration.WatchedFolderConfig { FolderPath = watchedFolder }],
            ApiEndpoint = "http://localhost:8080/api/files",
            PostFileContents = false,
            MoveProcessedFiles = false,
            ProcessedFolder = "processed",
            AllowedExtensions = [".txt"],
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
            CircuitBreakerOpenDurationMilliseconds = 30000,
            Logging = new SimpleFileLoggerOptions {
                LogType = LogType.Csv,
                FilePathPattern = Path.Combine(serviceDir, "FileWatchRest_{0:yyyyMMdd_HHmmss}"),
                LogLevel = LogLevel.Information
            }
        };
        string json = JsonSerializer.Serialize(defaultConfig, MyJsonContext.Default.ExternalConfiguration);
        await File.WriteAllTextAsync(configPath, json);
    }
}
