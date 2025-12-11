namespace FileWatchRest.Tests;

public class ConfigurationServiceMigrationTests : IDisposable {
    private readonly string _serviceNamePrefix = "FileWatchRest_Test_Migrate_" + Guid.NewGuid().ToString("N");
    private static readonly string[] value = ["C:/temp"];
    private static readonly JsonSerializerOptions s_jsonOptions = new() { WriteIndented = true };

    [Fact]
    public async Task LoadConfigurationAsyncMigratesTopLevelLogLevelToLoggingSection() {
        string serviceName = _serviceNamePrefix + "_TopLevel";
        string dir = Path.Combine(Path.GetTempPath(), serviceName);
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "FileWatchRest.json");

        string oldJson = JsonSerializer.Serialize(new {
            ApiEndpoint = "http://localhost:8080/api/files",
            Folders = value,
            LogLevel = "Debug",
            Logging = new {
                LogLevel = (string?)null,
                LogType = "Csv",
                FilePathPattern = "logs/FileWatchRest_{0:yyyyMMdd_HHmmss}"
            }
        }, s_jsonOptions);

        await File.WriteAllTextAsync(path, oldJson);

        string before = await File.ReadAllTextAsync(path);
        Assert.Contains("\"LogLevel\": \"Debug\"", before);

        ILoggerFactory loggerFactory = LoggerFactory.Create(_ => { });
        ILogger<ExternalConfigurationOptionsMonitor> monitorLogger = loggerFactory.CreateLogger<ExternalConfigurationOptionsMonitor>();
        var monitor = new ExternalConfigurationOptionsMonitor(path, monitorLogger, loggerFactory);
        ExternalConfiguration cfg = monitor.CurrentValue;

        Assert.NotNull(cfg);
        Assert.NotNull(cfg.Logging);

        string auditPath = Path.Combine(dir, "migration-audit.log");
        Assert.True(File.Exists(auditPath));
        string auditContent = await File.ReadAllTextAsync(auditPath);
        Assert.Contains("TopLevel LogLevel -> Logging.LogLevel", auditContent);

        try { File.Delete(path); Directory.Delete(dir); } catch { }
    }

    [Fact]
    public async Task LoadConfigurationAsyncMigratesLoggingMinimumLevelToLoggingLogLevel() {
        string serviceName = _serviceNamePrefix + "_LegacyMinimum";
        string dir = Path.Combine(Path.GetTempPath(), serviceName);
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "FileWatchRest.json");

        string oldJson = JsonSerializer.Serialize(new {
            ApiEndpoint = "http://localhost:8080/api/files",
            Folders = value,
            Logging = new { MinimumLevel = "Warning", LogType = "Csv", FilePathPattern = "logs/FileWatchRest_{0:yyyyMMdd_HHmmss}", LogLevel = (string?)null }
        }, s_jsonOptions);

        await File.WriteAllTextAsync(path, oldJson);

        string before2 = await File.ReadAllTextAsync(path);
        Assert.Contains("\"MinimumLevel\": \"Warning\"", before2);

        ILoggerFactory loggerFactory = LoggerFactory.Create(_ => { });
        ILogger<ExternalConfigurationOptionsMonitor> monitorLogger = loggerFactory.CreateLogger<ExternalConfigurationOptionsMonitor>();
        var monitor = new ExternalConfigurationOptionsMonitor(path, monitorLogger, loggerFactory);
        ExternalConfiguration cfg = monitor.CurrentValue;

        Assert.NotNull(cfg);
        Assert.NotNull(cfg.Logging);

        string auditPath2 = Path.Combine(dir, "migration-audit.log");
        Assert.True(File.Exists(auditPath2));
        string auditContent2 = await File.ReadAllTextAsync(auditPath2);
        Assert.Contains("Logging.MinimumLevel -> Logging.LogLevel", auditContent2);

        try { File.Delete(path); Directory.Delete(dir); } catch { }
    }

    public void Dispose() {
        string root = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        foreach (string? d in Directory.EnumerateDirectories(root).Where(p => Path.GetFileName(p).StartsWith("FileWatchRest_Test_Migrate_", StringComparison.Ordinal))) {
            try { Directory.Delete(d, true); } catch { }
        }
        GC.SuppressFinalize(this);
    }
}
