namespace FileWatchRest.Tests;

public class ExamplesConfigTests {
    private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };
    static ExamplesConfigTests() {
        _jsonOptions.Converters.Add(new JsonStringEnumConverter());
    }
    [Fact]
    public void ExampleConfigFilesLoadAndValidate() {
        // Arrange - locate examples folder by walking up parent directories
        string? examplesDir = null;
        string probe = AppContext.BaseDirectory;
        for (int i = 0; i < 10; i++) {
            string candidate = Path.Combine(probe, "examples");
            if (Directory.Exists(candidate)) { examplesDir = Path.GetFullPath(candidate); break; }
            string? parent = Path.GetDirectoryName(probe);
            if (string.IsNullOrEmpty(parent)) break;
            probe = parent;
        }
        Assert.NotNull(examplesDir);

        var logger = new Mock<ILogger<ExternalConfigurationOptionsMonitor>>();
        using var loggerFactory = new LoggerFactory();

        foreach (string file in Directory.GetFiles(examplesDir!, "*.json")) {
            // Copy to temp path so the monitor may write/migrate safely during the test
            string temp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".json");
            File.Copy(file, temp);

            try {
                // Deserialize directly to avoid any file-watcher side-effects during test
                string fileJson = File.ReadAllText(temp);
                ExternalConfiguration? cfg = JsonSerializer.Deserialize<ExternalConfiguration>(fileJson, _jsonOptions);
                cfg ??= new ExternalConfiguration();

                // Validate
                ValidationResult validation = ExternalConfigurationValidator.Validate(cfg);
                if (!validation.IsValid) {
                    Console.WriteLine($"Validation failed for {file}: {string.Join(';', validation.Errors.Select(e => e.ErrorMessage))}");
                    try { Console.WriteLine(JsonSerializer.Serialize(cfg, MyJsonContext.Default.ExternalConfiguration)); } catch { }
                    try { Console.WriteLine(File.ReadAllText(file)); } catch { }
                }
                Assert.True(validation.IsValid, $"Config from {Path.GetFileName(file)} must validate. Errors: {string.Join(';', validation.Errors.Select(e => e.ErrorMessage))}");

                // Ensure folders reference actions that exist
                foreach (ExternalConfiguration.WatchedFolderConfig f in cfg.Folders) {
                    Assert.True(cfg.Actions.Any(a => string.Equals(a.Name, f.ActionName, StringComparison.OrdinalIgnoreCase)), $"Folder {f.FolderPath} references missing action {f.ActionName}");
                }
            }
            finally {
                try { File.Delete(temp); } catch { }
            }
        }
    }
}
