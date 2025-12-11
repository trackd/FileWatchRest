namespace FileWatchRest.Tests.Action;

public class PowershellScriptActionTests {
    [Fact]
    public async Task ExecuteAsync_PowerShellScriptFilePath() {
        var fileEvent = new FileEventRecord("C:\\temp\\filePath.txt", DateTimeOffset.UtcNow, false, null);
        var args = new List<string> {
            "-File",
            "{FilePath}"
            };

        string scriptPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "TestUtilities", "PowershellAction.ps1"));
        string scriptDir = Path.GetDirectoryName(scriptPath) ?? AppContext.BaseDirectory;
        string outputPath = Path.Combine(scriptDir, "output_file.json");
        if (File.Exists(outputPath)) {
            File.Delete(outputPath);
        }

        // Require the test PowerShell script to be present; fail otherwise
        Assert.True(File.Exists(scriptPath), "PowerShell test script is missing in this environment");

        var testLogger = new TestLogger<PowerShellScriptAction>();
        var action = new PowerShellScriptAction(scriptPath, args, testLogger, 1000);

        // execute the action which will run the real script and write output.txt
        await action.ExecuteAsync(fileEvent, CancellationToken.None);

        // verify the script wrote the expected path to output_file.json (wait up to 2s)
        Assert.True(await WaitForFileAsync(outputPath, 5000));
        var json = JsonDocument.Parse(File.ReadAllText(outputPath));
        Assert.Equal(fileEvent.Path, json.RootElement.GetProperty("File").GetString());
        // verify output was logged
        Assert.Contains(testLogger.Entries, e => e.EventId.Id == 701 && e.Message.Contains(scriptPath));
        // cleanup
        File.Delete(outputPath);
    }

    [Fact]
    public async Task ExecuteAsync_PowerShellScriptFileObject() {
        var fileEvent = new FileEventRecord("C:\\temp\\fileObject.txt", DateTimeOffset.UtcNow, false, null);
        var args = new List<string> {
            "-Json",
            "{FileNotification:json}"
            };

        string scriptPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "TestUtilities", "PowershellAction.ps1"));
        string scriptDir = Path.GetDirectoryName(scriptPath) ?? AppContext.BaseDirectory;
        string outputPath = Path.Combine(scriptDir, "output_json.json");
        if (File.Exists(outputPath)) {
            File.Delete(outputPath);
        }

        // Require the test PowerShell script to be present; fail otherwise
        Assert.True(File.Exists(scriptPath), "PowerShell test script is missing in this environment");

        var testLogger = new TestLogger<PowerShellScriptAction>();
        var action = new PowerShellScriptAction(scriptPath, args, testLogger);

        // execute the action which will run the real script and write output.txt
        await action.ExecuteAsync(fileEvent, CancellationToken.None);

        // verify the script wrote the expected path to output_json.json (wait up to 2s)
        Assert.True(await WaitForFileAsync(outputPath, 5000));
        var json = JsonDocument.Parse(File.ReadAllText(outputPath));
        Assert.Equal(fileEvent.Path, json.RootElement.GetProperty("Path").GetString());
        // verify output was logged
        Assert.Contains(testLogger.Entries, e => e.EventId.Id == 701 && e.Message.Contains(scriptPath));
        // cleanup
        File.Delete(outputPath);
    }

    private static async Task<bool> WaitForFileAsync(string path, int timeoutMs = 2000) {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs) {
            if (File.Exists(path)) return true;
            await Task.Delay(50).ConfigureAwait(false);
        }
        return File.Exists(path);
    }
}
