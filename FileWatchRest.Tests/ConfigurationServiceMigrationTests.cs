namespace FileWatchRest.Tests;

public class ConfigurationServiceMigrationTests : IDisposable
{
    private readonly string _serviceNamePrefix = "FileWatchRest_Test_Migrate_" + Guid.NewGuid().ToString("N");
    private readonly Mock<ILogger<ConfigurationService>> _loggerMock = new();
    private static readonly string[] value = ["C:\\temp\\watch"];

    [Fact]
    public async Task LoadConfigurationAsync_Migrates_TopLevel_LogLevel_To_LoggingSection()
    {
        var serviceName = _serviceNamePrefix + "_TopLevel";
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), serviceName);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "FileWatchRest.json");

        var oldJson = JsonSerializer.Serialize(new
        {
            Folders = value,
            ApiEndpoint = "http://localhost:8080/api/files",
            ProcessedFolder = "processed",
            DiagnosticsUrlPrefix = "http://localhost:5005/",
            LogLevel = "Debug",
            Logging = new { LogType = "Csv", FilePathPattern = "logs/FileWatchRest_{0:yyyyMMdd_HHmmss}" }
        }, new JsonSerializerOptions { WriteIndented = true });

        await File.WriteAllTextAsync(path, oldJson);

        var before = await File.ReadAllTextAsync(path);
        before.Should().Contain("\"LogLevel\": \"Debug\"");

        var svc = new ConfigurationService(_loggerMock.Object, serviceName);
        var cfg = await svc.LoadConfigurationAsync();

        // Ensure we didn't fall back to defaults due to validation failure
        cfg.ApiEndpoint.Should().Be("http://localhost:8080/api/files");
        cfg.Should().NotBeNull();
        cfg.Logging.Should().NotBeNull();
        cfg.Logging.LogLevel.Should().Be("Debug");

        // Audit file should be created
        var auditPath = Path.Combine(dir, "migration-audit.log");
        File.Exists(auditPath).Should().BeTrue();
        var auditContent = await File.ReadAllTextAsync(auditPath);
        auditContent.Should().Contain("TopLevel LogLevel -> Logging.LogLevel");

        // Cleanup
        try { File.Delete(path); Directory.Delete(dir); } catch { }
    }

    [Fact]
    public async Task LoadConfigurationAsync_Migrates_Logging_MinimumLevel_To_Logging_LogLevel()
    {
        var serviceName = _serviceNamePrefix + "_LegacyMinimum";
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), serviceName);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "FileWatchRest.json");

        var oldJson = JsonSerializer.Serialize(new
        {
            Folders = new[] { "C:\\temp\\watch" },
            ApiEndpoint = "http://localhost:8080/api/files",
            ProcessedFolder = "processed",
            DiagnosticsUrlPrefix = "http://localhost:5005/",
            Logging = new { MinimumLevel = "Warning", LogType = "Csv", FilePathPattern = "logs/FileWatchRest_{0:yyyyMMdd_HHmmss}" }
        }, new JsonSerializerOptions { WriteIndented = true });

        await File.WriteAllTextAsync(path, oldJson);

        var before2 = await File.ReadAllTextAsync(path);
        before2.Should().Contain("\"MinimumLevel\": \"Warning\"");

        var svc = new ConfigurationService(_loggerMock.Object, serviceName);
        var cfg = await svc.LoadConfigurationAsync();

        // Ensure we didn't fall back to defaults due to validation failure
        cfg.ApiEndpoint.Should().Be("http://localhost:8080/api/files");
        cfg.Should().NotBeNull();
        cfg.Logging.Should().NotBeNull();
        cfg.Logging.LogLevel.Should().Be("Warning");

        var auditPath2 = Path.Combine(dir, "migration-audit.log");
        File.Exists(auditPath2).Should().BeTrue();
        var auditContent2 = await File.ReadAllTextAsync(auditPath2);
        auditContent2.Should().Contain("Logging.MinimumLevel -> Logging.LogLevel");

        try { File.Delete(path); Directory.Delete(dir); } catch { }
    }

    public void Dispose()
    {
        // best-effort cleanup of any created folders with our prefix
        var root = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        foreach (var d in Directory.EnumerateDirectories(root).Where(p => Path.GetFileName(p).StartsWith("FileWatchRest_Test_Migrate_")))
        {
            try { Directory.Delete(d, true); } catch { }
        }
    }
}
