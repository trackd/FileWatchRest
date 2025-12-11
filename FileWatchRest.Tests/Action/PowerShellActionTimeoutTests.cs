namespace FileWatchRest.Tests.Action;

public class PowerShellActionTimeoutTests {
    [Fact]
    public async Task ExecuteAsync_LongRunningScript_LogsTimeout() {
        string scriptPath = Path.GetTempFileName() + ".ps1";
        try {
            File.WriteAllText(scriptPath, "Start-Sleep -Seconds 5; Write-Output 'done'");

            var fileEvent = new FileEventRecord("C:\\temp\\file_ps_timeout.txt", DateTimeOffset.UtcNow, false, null);
            var logger = new TestLogger<PowerShellScriptAction>();
            var action = new PowerShellScriptAction(scriptPath, [], logger, executionTimeoutMilliseconds: 1000, ignoreOutput: true);

            await action.ExecuteAsync(fileEvent, CancellationToken.None);

            Assert.Contains(logger.Entries, e => e.EventId.Id == 703);
        }
        finally {
            try { File.Delete(scriptPath); } catch { }
        }
    }
}
