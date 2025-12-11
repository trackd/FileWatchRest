namespace FileWatchRest.Tests.Action;

public class ExitCodeLoggingTests {
    [Fact]
    public async Task ExecutableAction_LogsExitCode_WhenProcessExits() {
        // Arrange: use cmd to exit with a specific code
        string exe = "cmd";
        var args = new List<string> { "/c", "exit", "42" };
        var logger = new TestLogger<ExecutableAction>();
        var action = new ExecutableAction(exe, args, logger, executionTimeoutMilliseconds: 5000, ignoreOutput: true);
        var record = new FileEventRecord { Path = "C:\\nonexistent.txt", Timestamp = DateTimeOffset.Now };

        // Act
        await action.ExecuteAsync(record, CancellationToken.None);

        // Assert: find ExecutableExitCode event with exit code 42
        var exitEvents = logger.Entries.Where(e => e.EventId.Id == 709).ToList();
        Assert.NotEmpty(exitEvents);
        Assert.Contains(exitEvents, e => e.Message != null && e.Message.Contains("42"));
    }

    [Fact]
    public async Task PowerShellScriptAction_LogsExitCode_WhenScriptExits() {
        // Arrange: create a temporary PowerShell script that exits with code 7
        string tempDir = Path.Combine(Path.GetTempPath(), "fwr-tests");
        Directory.CreateDirectory(tempDir);
        string scriptPath = Path.Combine(tempDir, "exit7.ps1");
        await File.WriteAllTextAsync(scriptPath, "exit 7");

        var logger = new TestLogger<PowerShellScriptAction>();
        // Inject resolver to use powershell.exe deterministicly (Windows agents)
        static string? resolver(string name) {
            return "powershell.exe";
        }

        var action = new PowerShellScriptAction(scriptPath, arguments: null, logger: logger, executionTimeoutMilliseconds: 5000, ignoreOutput: true, executableResolver: resolver);
        var record = new FileEventRecord { Path = scriptPath, Timestamp = DateTimeOffset.Now };

        // Act
        await action.ExecuteAsync(record, CancellationToken.None);

        // Assert: PowerShellExitCode event should be present and contain 7
        var exitEvents = logger.Entries.Where(e => e.EventId.Id == 708).ToList();
        Assert.NotEmpty(exitEvents);
        Assert.Contains(exitEvents, e => e.Message != null && e.Message.Contains('7'));
    }

    [Fact]
    public async Task PowerShellScriptAction_LogsExitCode_WhenScriptExits_UsingDefaultResolver() {
        // Arrange: create a temporary PowerShell script that exits with code 9
        string tempDir = Path.Combine(Path.GetTempPath(), "fwr-tests");
        Directory.CreateDirectory(tempDir);
        string scriptPath = Path.Combine(tempDir, "exit9.ps1");
        await File.WriteAllTextAsync(scriptPath, "exit 9");

        var logger = new TestLogger<PowerShellScriptAction>();
        // Do NOT inject resolver; let the action pick pwsh or powershell.exe from PATH
        var action = new PowerShellScriptAction(scriptPath, arguments: null, logger: logger, executionTimeoutMilliseconds: 5000, ignoreOutput: true);
        var record = new FileEventRecord { Path = scriptPath, Timestamp = DateTimeOffset.Now };

        // Act
        await action.ExecuteAsync(record, CancellationToken.None);

        // Assert: PowerShellExitCode event should be present and contain 9
        var exitEvents = logger.Entries.Where(e => e.EventId.Id == 708).ToList();
        Assert.NotEmpty(exitEvents);
        Assert.Contains(exitEvents, e => e.Message != null && e.Message.Contains('9'));
    }
}
