namespace FileWatchRest.Tests.Configuration;

public class ExternalConfigurationOptionsMonitorLockTests : IDisposable {
    private readonly string _testConfigDirectory;
    private readonly ILoggerFactory _loggerFactory;

    public ExternalConfigurationOptionsMonitorLockTests() {
        _testConfigDirectory = Path.Combine(Path.GetTempPath(), $"FileWatchRest_MonitorLockTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testConfigDirectory);
        _loggerFactory = LoggerFactory.Create(builder => builder.AddDebug().SetMinimumLevel(LogLevel.Trace));
    }

    public void Dispose() {
        try { if (Directory.Exists(_testConfigDirectory)) Directory.Delete(_testConfigDirectory, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task SaveWhileFileLocked_LogsLoadFailure_NotDecryptFailure() {
        if (!OperatingSystem.IsWindows()) {
            return; // This test targets the Windows-only encryption/save path
        }

        string configPath = Path.Combine(_testConfigDirectory, "locked_config.json");

        string content = /*lang=json,strict*/ @"{
            ""ApiEndpoint"": ""http://localhost/api"",
            ""Folders"": [],
            ""DiagnosticsBearerToken"": ""plain-diagnostics-token-xyz"",
            ""Actions"": [
                { ""Name"": ""act1"", ""ActionType"": ""RestPost"", ""ApiEndpoint"": ""http://localhost"", ""BearerToken"": ""plain-action-token-abc"" }
            ],
            ""Logging"": { ""LogLevel"": ""Information"" }
        }";

        await File.WriteAllTextAsync(configPath, content);

        // Open an exclusive lock on the file to simulate another process holding it
        using var lockFs = new FileStream(configPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var testLogger = new TestLogger<ExternalConfigurationOptionsMonitor>();

        // Constructing the monitor will attempt to load and (on Windows) persist encrypted tokens
        var monitor = new ExternalConfigurationOptionsMonitor(configPath, testLogger, _loggerFactory);

        // Allow constructor and any async operations a moment
        await Task.Delay(300);

        // Look for the loader-level failure (constructor should catch the IO and log FailedToLoadInitial)
        bool sawFailedToLoadInitial = testLogger.Entries.Any(e => e.EventId.Id == 201);
        bool sawFailedToDecryptToken = testLogger.Entries.Any(e => e.EventId.Id == 616);

        Assert.True(sawFailedToLoadInitial, "Expected a load-initial failure log when file is locked during initialization");
        Assert.False(sawFailedToDecryptToken, "Should not log a failed-to-decrypt-token when the real error is an IO lock");
    }
}
