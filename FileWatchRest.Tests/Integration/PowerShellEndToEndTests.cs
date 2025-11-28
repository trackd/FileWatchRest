namespace FileWatchRest.Tests.Integration;

public class PowerShellEndToEndTests : IDisposable {
    private readonly string _baseDir;
    private readonly string _watchDir;
    private readonly string _scriptPath;
    private readonly string _configPath;

    public PowerShellEndToEndTests() {
        _baseDir = Path.Combine(Path.GetTempPath(), "FileWatchRest.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_baseDir);
        _watchDir = Path.Combine(_baseDir, "watch");
        Directory.CreateDirectory(_watchDir);
        _scriptPath = Path.Combine(_baseDir, "Move-ArchiveFiles.ps1");
        _configPath = Path.Combine(_baseDir, "FileWatchRest.json");
    }

    [Fact]
    public async Task EndToEnd_PowerShellScript_Invoked_When_FileCreated_UsingStringEnumConfig() {
        // Write a simple PowerShell script that drops a marker file when executed
        string marker = Path.Combine(_baseDir, "marker.txt");
        string script = $"Set-Content -Path \"{marker}\" -Value \"ok\" -Force\nExit 0";
        await File.WriteAllTextAsync(_scriptPath, script);

        // Build typed configuration and serialize (ensures enum is written as string)
        var cfg = new ExternalConfiguration {
            Folders = [new() { FolderPath = _watchDir, ActionName = "MoveArchiveFiles" }],
            ApiEndpoint = string.Empty,
            Actions = [
                new() {
                        Name = "MoveArchiveFiles",
                        ActionType = ExternalConfiguration.FolderActionType.PowerShellScript,
                        ScriptPath = _scriptPath,
                        Arguments = []
                    }
            ]
        };

        string json = JsonSerializer.Serialize(cfg, MyJsonContext.Default.ExternalConfiguration);
        await File.WriteAllTextAsync(_configPath, json);

        // Start options monitor to load config from file
        var cfgLogger = new TestLogger<ExternalConfigurationOptionsMonitor>();
        var monitor = new ExternalConfigurationOptionsMonitor(_configPath, cfgLogger, NullLoggerFactory.Instance);

        // Diagnostics and watcher
        var diagLogger = new TestLogger<DiagnosticsService>();
        var diagnostics = new DiagnosticsService(diagLogger, monitor);
        var fwLogger = new TestLogger<FileWatcherManager>();
        var manager = new FileWatcherManager(fwLogger, diagnostics);

        // Configure actions from loaded configuration and start watching
        manager.ConfigureFolderActions(monitor.CurrentValue.Folders, monitor.CurrentValue, WorkerFactory.CreateWorker(optionsMonitor: monitor));
        await manager.StartWatchingAsync(monitor.CurrentValue.Folders, monitor.CurrentValue, (s, e) => { }, null, null);

        // Create a file in the watched directory to trigger the action
        string testFile = Path.Combine(_watchDir, "test.txt");
        await File.WriteAllTextAsync(testFile, "hello");

        // Wait for marker file to appear (give some generous timeout)
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromSeconds(10)) {
            if (File.Exists(marker)) break;
            await Task.Delay(200);
        }

        Assert.True(File.Exists(marker), "PowerShell script did not create marker file");

        // Cleanup watcher
        await manager.StopAllAsync();
    }

    public void Dispose() {
        try { Directory.Delete(_baseDir, true); } catch { }
        GC.SuppressFinalize(this);
    }
}
